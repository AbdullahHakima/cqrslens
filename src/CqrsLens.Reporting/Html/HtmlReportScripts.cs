namespace CqrsLens;

/// <summary>Embedded Mermaid bootstrap script for the portable HTML report.</summary>
internal static class HtmlReportScripts
{
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
