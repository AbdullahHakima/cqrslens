using System.Text;

namespace CqrsLens;

/// <summary>Embedded CSS and Mermaid bootstrap assets for the portable HTML report.</summary>
internal static class HtmlReportAssets
{
    internal const string MermaidVersion = "12.2.0";
    internal const string MermaidIntegrity =
        "sha384-8xUhuLNMjW/cI2Dacg3DR4WliYznvIg0LVG1stctThKD/YVrJFI9TNCFxft5NYgp";

    internal const string Css = """
        :root { color-scheme: light dark; }
        * { box-sizing: border-box; }
        body { margin: 0; font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
               background: #f6f7f9; color: #1c1e21; }
        main { max-width: 1080px; margin: 0 auto; padding: 2rem 1.25rem 3rem; }
        h1 { font-size: 1.8rem; margin: 0 0 .25rem; }
        h2 { font-size: 1.25rem; margin: 2rem 0 .75rem; border-bottom: 1px solid #d6d9de; padding-bottom: .35rem; }
        h3 { font-size: 1.05rem; margin: 1.4rem 0 .5rem; }
        h4 { font-size: .88rem; text-transform: uppercase; letter-spacing: .04em; color: #5a6068; margin: 0 0 .5rem; }
        .meta, .footer, .note { color: #5a6068; font-size: .9rem; }
        .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: .75rem; margin: 1.25rem 0 .5rem; }
        .card { background: #ffffff; border: 1px solid #d6d9de; border-radius: 10px; padding: 1rem; text-align: center; }
        .card-value { font-size: 1.9rem; font-weight: 700; line-height: 1.1; }
        .card-label { color: #5a6068; font-size: .85rem; margin-top: .25rem; }
        figure.diagram { margin: 0 0 1.25rem; }
        figure.diagram figcaption { color: #5a6068; font-size: .85rem; margin-top: .5rem; }
        pre.mermaid, .svg-diagram { background: #ffffff; border: 1px solid #d6d9de; border-radius: 10px; padding: 1rem; overflow-x: auto; }
        .svg-diagram svg { display: block; max-width: 100%; height: auto; margin: 0 auto; }
        ul.legend { display: flex; flex-wrap: wrap; gap: .35rem 1rem; list-style: none; padding: 0; margin: .5rem 0 1rem; font-size: .88rem; }
        ul.legend li { display: flex; align-items: center; gap: .4rem; }
        .swatch { display: inline-block; width: 1rem; height: 1rem; border-radius: 4px; border: 2px solid; }
        .swatch.command { background: #e8f1ff; border-color: #2563eb; }
        .swatch.query { background: #e6fffa; border-color: #0f766e; }
        .swatch.request { background: #f5f3ff; border-color: #6d28d9; }
        .swatch.handler { background: #f8fafc; border-color: #475569; }
        .swatch.validator { background: #fff7ed; border-color: #c2410c; }
        .swatch.missing { background: #fef2f2; border-color: #dc2626; border-style: dashed; }
        .swatch.no-validator { background: #e8f1ff; border-color: #d97706; border-style: dashed; }
        .swatch.domain { background: #ecfdf5; border-color: #059669; }
        .swatch.application { background: #eff6ff; border-color: #2563eb; }
        .swatch.infrastructure { background: #fff7ed; border-color: #c2410c; }
        .swatch.api { background: #f5f3ff; border-color: #7c3aed; }
        .swatch.tests { background: #f8fafc; border-color: #64748b; }
        .swatch.violation { background: transparent; border-color: #dc2626; border-style: dashed; }
        .diagram-error { background: #fdecec; border: 1px solid #f0b4b4; border-radius: 8px; padding: .7rem; color: #7f1d1d; }
        details { margin: .35rem 0 1rem; }
        details pre { background: #eceef1; padding: .7rem; border-radius: 6px; overflow: auto; }
        table { border-collapse: collapse; width: 100%; background: #ffffff; }
        th, td { border: 1px solid #d6d9de; padding: .45rem .6rem; text-align: left; font-size: .92rem; }
        th { background: #eceef1; }
        a.req-link { font-weight: 600; text-decoration: none; }
        a.req-link:hover { text-decoration: underline; }
        .cqrs-explorer { margin: .75rem 0 1.5rem; }
        .filter-radio { position: absolute; opacity: 0; pointer-events: none; }
        .explorer-toolbar { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: .75rem; margin-bottom: .85rem; }
        .filter-pills { display: flex; flex-wrap: wrap; gap: .4rem; }
        .filter-pill { cursor: pointer; padding: .35rem .75rem; border-radius: 999px; font-size: .85rem; font-weight: 600; background: #ffffff; border: 1px solid #d6d9de; color: #334155; user-select: none; }
        #cqrs-filter-all:checked ~ .explorer-toolbar label[for="cqrs-filter-all"],
        #cqrs-filter-command:checked ~ .explorer-toolbar label[for="cqrs-filter-command"],
        #cqrs-filter-query:checked ~ .explorer-toolbar label[for="cqrs-filter-query"],
        #cqrs-filter-issues:checked ~ .explorer-toolbar label[for="cqrs-filter-issues"] { background: #2563eb; border-color: #2563eb; color: #ffffff; }
        #cqrs-filter-command:checked ~ .explorer-list details.explorer-item:not([data-kind="command"]) { display: none; }
        #cqrs-filter-query:checked ~ .explorer-list details.explorer-item:not([data-kind="query"]) { display: none; }
        #cqrs-filter-issues:checked ~ .explorer-list details.explorer-item:not([data-issues="true"]) { display: none; }
        .explorer-actions { display: flex; flex-wrap: wrap; align-items: center; gap: .45rem; flex: 1; justify-content: flex-end; }
        .explorer-search { padding: .4rem .7rem; border: 1px solid #d6d9de; border-radius: 8px; font-size: .88rem; min-width: 240px; max-width: 340px; width: 100%; background: #ffffff; color: inherit; }
        .explorer-btn { cursor: pointer; padding: .38rem .68rem; border: 1px solid #d6d9de; border-radius: 8px; background: #ffffff; color: inherit; font-size: .84rem; font-weight: 500; }
        .explorer-btn:hover { background: #eceef1; }
        details.explorer-item { background: #ffffff; border: 1px solid #d6d9de; border-radius: 10px; margin: 0 0 .55rem; overflow: hidden; transition: border-color .15s, box-shadow .15s; }
        details.explorer-item:target { border-color: #2563eb; box-shadow: 0 0 0 3px rgba(37, 99, 235, .2); }
        summary.explorer-summary { cursor: pointer; padding: .65rem .9rem; display: flex; flex-wrap: wrap; align-items: center; gap: .6rem; font-size: .93rem; list-style: none; }
        summary.explorer-summary::-webkit-details-marker { display: none; }
        .explorer-title { flex: 1; min-width: 180px; }
        .explorer-response { color: #5a6068; font-size: .86rem; }
        .explorer-meta { display: flex; flex-wrap: wrap; gap: .35rem; align-items: center; }
        .badge { display: inline-block; padding: .15rem .5rem; border-radius: 6px; font-size: .76rem; font-weight: 700; text-transform: uppercase; letter-spacing: .03em; }
        .badge-command { background: #e8f1ff; color: #1e40af; border: 1px solid #93c5fd; }
        .badge-query { background: #e6fffa; color: #115e59; border: 1px solid #5eead4; }
        .badge-request { background: #f5f3ff; color: #5b21b6; border: 1px solid #c4b5fd; }
        .meta-pill { font-size: .78rem; padding: .15rem .5rem; border-radius: 6px; background: #f1f5f9; color: #334155; }
        .meta-pill.status-error { background: #fdecec; color: #991b1b; }
        .meta-pill.status-warning { background: #fff6db; color: #92400e; }
        .explorer-body { border-top: 1px solid #e5e7eb; padding: .9rem; background: #fafbfc; }
        .explorer-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: .85rem; }
        .explorer-panel { background: #ffffff; border: 1px solid #e5e7eb; border-radius: 8px; padding: .75rem; }
        dl.explorer-dl { margin: 0; display: grid; grid-template-columns: 92px 1fr; row-gap: .3rem; column-gap: .5rem; font-size: .86rem; }
        dl.explorer-dl dt { color: #5a6068; font-weight: 600; }
        dl.explorer-dl dd { margin: 0; word-break: break-word; }
        ul.explorer-links { margin: 0; padding-left: 1.1rem; font-size: .86rem; }
        ul.explorer-links li { margin-bottom: .35rem; }
        .meta-sub { color: #5a6068; font-size: .8rem; }
        .explorer-empty { margin: 0; color: #5a6068; font-size: .85rem; font-style: italic; }
        ul.explorer-checks { margin: .75rem 0 0; }
        ul.checks { list-style: none; padding: 0; }
        ul.checks li { padding: .4rem .6rem; border-radius: 6px; margin-bottom: .35rem; }
        ul.checks li.error { background: #fdecec; }
        ul.checks li.warning { background: #fff6db; }
        ul.checks li.info { background: #e8f1ff; }
        code { background: #eceef1; padding: .1rem .3rem; border-radius: 4px; }
        hr { border: none; border-top: 1px solid #d6d9de; margin: 2rem 0 1rem; }
        a { color: #0b5fff; }
        .watermark-overlay { position: fixed; inset: 0; pointer-events: none; z-index: 9999; display: flex; align-items: center; justify-content: center; font-size: clamp(2.5rem, 6vw, 5.5rem); font-weight: 900; letter-spacing: .18em; text-transform: uppercase; color: rgba(99, 102, 241, .06); transform: rotate(-24deg); user-select: none; white-space: nowrap; }
        pre.ascii-watermark { background: linear-gradient(135deg, #0f172a 0%, #1e1b4b 100%); color: #38bdf8; border: 1px solid #312e81; border-radius: 10px; padding: 1rem 1.25rem; margin: 1rem 0; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: .78rem; line-height: 1.15; overflow-x: auto; text-align: center; box-shadow: 0 4px 14px rgba(15, 23, 42, .12); }
        @media (prefers-color-scheme: dark) {
          body { background: #16181c; color: #e6e8eb; }
          .card, table, pre.mermaid, .svg-diagram, details.explorer-item, .explorer-panel, .filter-pill, .explorer-search, .explorer-btn { background: #1f2227; border-color: #3a3e45; color: #e6e8eb; }
          .explorer-body { background: #181a1f; border-color: #3a3e45; }
          .meta-pill { background: #26292f; color: #cbd5e1; }
          .badge-command { background: #1e3a8a; color: #dbeafe; border-color: #3b82f6; }
          .badge-query { background: #134e4a; color: #ccfbf1; border-color: #14b8a6; }
          .badge-request { background: #4c1d95; color: #ede9fe; border-color: #8b5cf6; }
          th { background: #26292f; }
          th, td { border-color: #3a3e45; }
          h2 { border-color: #3a3e45; }
          code { background: #26292f; }
          ul.checks li.error { background: #3a2226; }
          ul.checks li.warning { background: #38331c; }
          ul.checks li.info { background: #1d2b3f; }
          .meta, .footer, .note, .card-label, figure.diagram figcaption, .explorer-response, .meta-sub, dl.explorer-dl dt, h4 { color: #9aa0a8; }
          .diagram-error { background: #3a2226; border-color: #7f1d1d; color: #fecaca; }
          details pre { background: #26292f; }
          hr { border-color: #3a3e45; }
          a { color: #60a5fa; }
        }
        """;

    internal const string MermaidBootstrapScript = """
                (async () => {
                  const searchInput = document.getElementById('cqrs-explorer-search');
                  const expandBtn = document.getElementById('cqrs-explorer-expand');
                  const collapseBtn = document.getElementById('cqrs-explorer-collapse');
                  const explorerItems = [...document.querySelectorAll('details.explorer-item')];
                  const tableRows = [...document.querySelectorAll('table.requests-table tbody tr')];

                  const openRequestInspector = (anchorId) => {
                    const target = document.getElementById(anchorId);
                    if (!target) return;
                    if (target.tagName === 'DETAILS') target.open = true;
                    target.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                    history.replaceState(null, '', '#' + anchorId);
                  };

                  if (searchInput) {
                    searchInput.addEventListener('input', () => {
                      const q = searchInput.value.trim().toLowerCase();
                      explorerItems.forEach((item) => {
                        const hay = (item.getAttribute('data-search') || '').toLowerCase();
                        item.hidden = q.length > 0 && !hay.includes(q);
                      });
                      tableRows.forEach((row) => {
                        const hay = (row.getAttribute('data-search') || '').toLowerCase();
                        row.hidden = q.length > 0 && !hay.includes(q);
                      });
                    });
                  }

                  if (expandBtn) {
                    expandBtn.addEventListener('click', () => {
                      explorerItems.forEach((item) => { if (!item.hidden) item.open = true; });
                    });
                  }
                  if (collapseBtn) {
                    collapseBtn.addEventListener('click', () => {
                      explorerItems.forEach((item) => { item.open = false; });
                    });
                  }

                  document.querySelectorAll('a.req-link').forEach((link) => {
                    link.addEventListener('click', (e) => {
                      const href = link.getAttribute('href') || '';
                      if (href.startsWith('#')) {
                        e.preventDefault();
                        openRequestInspector(href.slice(1));
                      }
                    });
                  });

                  const nodes = [...document.querySelectorAll('pre.mermaid')];
                  const showSource = (node, message) => {
                    const error = document.createElement('p');
                    error.className = 'diagram-error';
                    error.textContent = message;
                    const details = document.createElement('details');
                    const summary = document.createElement('summary');
                    summary.textContent = 'Show diagram source';
                    const pre = document.createElement('pre');
                    pre.textContent = node.textContent ?? '';
                    details.append(summary, pre);
                    node.replaceWith(error, details);
                  };
                  if (typeof mermaid === 'undefined') {
                    nodes.forEach((node) =>
                      showSource(node, 'Mermaid could not be loaded. The diagram source is shown below.'));
                    return;
                  }
                  try {
                    mermaid.initialize({
                      startOnLoad: false,
                      theme: 'default',
                      look: 'classic',
                      layout: 'elk',
                      maxTextSize: 50000,
                      maxEdges: 500,
                      securityLevel: 'strict',
                      suppressErrorRendering: true,
                      themeVariables: {
                        fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif'
                      }
                    });
                  } catch (error) {
                    nodes.forEach((node) =>
                      showSource(node, 'Mermaid could not be initialized. ' +
                        (error && error.message ? error.message : String(error))));
                    return;
                  }
                  const byName = new Map();
                  explorerItems.forEach((item) => {
                    const reqName = item.getAttribute('data-request-name');
                    const reqFull = item.getAttribute('data-request-fullname');
                    if (reqName && !byName.has(reqName)) byName.set(reqName, item.id);
                    if (reqFull) byName.set(reqFull, item.id);
                  });
                  for (let i = 0; i < nodes.length; i++) {
                    const node = nodes[i];
                    const source = node.textContent ?? '';
                    try {
                      const { svg } = await mermaid.render('cqrslens-diagram-' + i, source);
                      node.innerHTML = svg;
                      node.querySelectorAll('g.node').forEach((g) => {
                        const label = (g.textContent || '').trim();
                        const anchorId = byName.get(label);
                        if (anchorId) {
                          g.style.cursor = 'pointer';
                          g.addEventListener('click', () => openRequestInspector(anchorId));
                        }
                      });
                    } catch (error) {
                      showSource(node, 'This diagram could not be rendered. ' +
                        (error && error.message ? error.message : String(error)));
                    }
                  }
                })();
                """;
}
