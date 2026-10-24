import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { chromium } from "playwright";

const require = createRequire(import.meta.url);
const mermaidBundle = require.resolve("mermaid/dist/mermaid.min.js");

const files = process.argv.slice(2);
if (files.length === 0) {
  console.error("usage: node check.mjs <report.md> [report.md ...]");
  process.exit(2);
}

const LIMIT_TEXT = 45_000;
const LIMIT_EDGES = 400;
const htmlEntity = /&(?:#\d+|#x[\da-f]+|[a-z][a-z0-9]+);/gi;
const namedEntities = { quot: '"', lt: "<", gt: ">", amp: "&" };

function decodeHtmlOnce(value) {
  return value.replace(/&(#x[\da-f]+|#\d+|quot|lt|gt|amp);/gi, (_, entity) => {
    if (entity.startsWith("#x") || entity.startsWith("#X")) {
      return String.fromCodePoint(parseInt(entity.slice(2), 16));
    }
    if (entity.startsWith("#")) {
      return String.fromCodePoint(parseInt(entity.slice(1), 10));
    }
    return namedEntities[entity.toLowerCase()];
  });
}

function countEdges(source) {
  return source
    .split(/\r?\n/)
    .filter(
      (line) =>
        line.includes("-->") ||
        line.includes("-.->") ||
        line.includes("||--") ||
        line.includes("|o--") ||
        line.includes("}o--"),
    ).length;
}

const browser = await chromium.launch();
const page = await browser.newPage();
const pageErrors = [];
page.on("pageerror", (error) => pageErrors.push(error.message));
await page.setContent("<!doctype html><html><body></body></html>");
await page.addScriptTag({ path: mermaidBundle });
await page.evaluate(() => {
  window.mermaid.initialize({
    startOnLoad: false,
    theme: "default",
    look: "classic",
    layout: "elk",
    maxTextSize: 50000,
    maxEdges: 500,
    securityLevel: "strict",
    suppressErrorRendering: true,
  });
});

let checked = 0;
let passed = 0;
let failures = 0;

for (const file of files) {
  const report = fs.readFileSync(file, "utf8");
  const blocks = [...report.matchAll(/```mermaid\r?\n([\s\S]*?)\r?\n```/g)]
    .map((match) => match[1]);

  if (blocks.length === 0) {
    console.error(`FAIL ${file}: no Mermaid code blocks`);
    failures += 1;
    continue;
  }

  for (let i = 0; i < blocks.length; i += 1) {
    checked += 1;
    const label = `${path.basename(file)} block ${i + 1}`;
    const source = blocks[i];

    const entities = [...source.matchAll(htmlEntity)].map((match) => match[0]);
    if (entities.length > 0) {
      console.error(
        `FAIL ${label}: standard HTML entities are decoded by GitHub before Mermaid: ${entities.join(", ")}`,
      );
      failures += 1;
      continue;
    }

    // GitHub HTML-decodes rendered Markdown before passing it to Mermaid.
    // CqrsLens deliberately emits Mermaid-native entities (#quot;, #lt;, ...)
    // and no standard HTML entities, so this must be a no-op.
    const decoded = decodeHtmlOnce(source);
    if (decoded !== source) {
      console.error(`FAIL ${label}: source changes after GitHub-style HTML decoding`);
      failures += 1;
      continue;
    }

    if (source.length > LIMIT_TEXT) {
      console.error(
        `FAIL ${label}: ${source.length} chars exceeds the ${LIMIT_TEXT}-char CqrsLens guard`,
      );
      failures += 1;
      continue;
    }

    const edges = countEdges(source);
    if (edges > LIMIT_EDGES) {
      console.error(`FAIL ${label}: ${edges} edges exceeds the ${LIMIT_EDGES}-edge guard`);
      failures += 1;
      continue;
    }

    try {
      const result = await page.evaluate(
        async ({ source, id }) => {
          await window.mermaid.parse(source);
          const { svg } = await window.mermaid.render(id, source);
          return {
            svgLength: svg.length,
            viewBox: /viewBox="([^"]+)"/.exec(svg)?.[1] ?? null,
          };
        },
        { source: decoded, id: `cqrslens-render-check-${checked}` },
      );

      if (result.svgLength === 0) {
        throw new Error("renderer returned no SVG");
      }

      console.log(
        `OK ${label}: ${source.length} chars, ${edges} edges, ` +
          `${result.svgLength} SVG chars, viewBox ${result.viewBox}`,
      );
      passed += 1;
    } catch (error) {
      const message = error && error.message ? error.message : String(error);
      console.error(`FAIL ${label}: ${message}`);
      failures += 1;
    }
  }
}

if (pageErrors.length > 0) {
  console.error(`FAIL browser page errors: ${pageErrors.join("; ")}`);
  failures += 1;
}

await browser.close();

console.log(`${passed}/${checked} diagrams passed`);
if (failures > 0) {
  process.exitCode = 1;
}
