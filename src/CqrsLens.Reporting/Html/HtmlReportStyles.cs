namespace CqrsLens;

/// <summary>Embedded CSS for the portable HTML report across light and dark modes.</summary>
internal static class HtmlReportStyles
{
    internal const string Css = """
        :root { color-scheme: light dark; }
        * { box-sizing: border-box; }
        body { margin: 0; font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
               background: #f4f6f9; color: #1c1e21; line-height: 1.55; }
        .portal-shell { display: grid; grid-template-columns: 220px minmax(0, 1fr); gap: 1.5rem;
                        max-width: 1520px; margin: 0 auto; padding: 1.5rem 1.25rem 3rem; align-items: start; }
        nav.toc { position: sticky; top: 1rem; background: #ffffff; border: 1px solid #dce1e8;
                  border-radius: 10px; padding: .9rem 1rem; font-size: .88rem;
                  box-shadow: 0 1px 2px rgba(15, 23, 42, .04); }
        .toc-title { font-size: .78rem; font-weight: 700; text-transform: uppercase; letter-spacing: .06em;
                     color: #64748b; margin-bottom: .5rem; }
        nav.toc ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: .35rem; }
        nav.toc a { text-decoration: none; font-weight: 500; display: block; padding: .24rem .42rem; border-radius: 6px; }
        nav.toc a:hover { background: #eef2ff; }
        main { max-width: 1260px; width: 100%; min-width: 0; margin: 0 auto; padding: 1.5rem 1.25rem 3rem; }
        .portal-shell main { margin: 0; padding: 0; }
        @media (max-width: 959px) {
          .portal-shell { display: block; padding: 1rem .9rem 2.5rem; }
          nav.toc { position: static; margin-bottom: 1.25rem; }
          nav.toc ul { flex-direction: row; flex-wrap: wrap; gap: .35rem .55rem; }
        }
        .service-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: .95rem; margin: 1rem 0 1.35rem; }
        .service-card { background: #ffffff; border: 1px solid #dce1e8; border-radius: 10px; padding: 1rem 1.05rem;
                        display: flex; flex-direction: column; gap: .6rem;
                        box-shadow: 0 1px 2px rgba(15, 23, 42, .04); min-width: 0; }
        .service-card-header { display: flex; align-items: flex-start; justify-content: space-between; gap: .5rem; }
        .service-card-header strong { min-width: 0; flex: 1; overflow-wrap: anywhere; word-break: break-word; font-size: .98rem; line-height: 1.35; }
        .role-pill { display: inline-block; flex-shrink: 0; padding: .15rem .55rem; border-radius: 999px; font-size: .74rem;
                     font-weight: 700; text-transform: uppercase; letter-spacing: .04em; }
        .role-api { background: #eff6ff; color: #1e40af; border: 1px solid #93c5fd; }
        .role-worker { background: #fff7ed; color: #9a3412; border: 1px solid #fdba74; }
        .role-ui { background: #fdf4ff; color: #86198f; border: 1px solid #f0abfc; }
        .role-orchestrator { background: #f5f3ff; color: #5b21b6; border: 1px solid #c4b5fd; }
        .role-library, .role-tests { background: #f1f5f9; color: #334155; border: 1px solid #cbd5e1; }
        .tech-badges, .service-metrics { display: flex; flex-wrap: wrap; gap: .35rem; align-items: center; }
        .tech-badge { font-size: .76rem; padding: .14rem .48rem; border-radius: 5px; background: #eef2ff; color: #3730a3; border: 1px solid #c7d2fe; }
        .start-here { font-size: .82rem; color: #475569; line-height: 1.45; }
        .start-here-title { display: block; font-size: .72rem; font-weight: 700; text-transform: uppercase; letter-spacing: .05em; color: #64748b; margin-bottom: .3rem; }
        ul.start-here-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: .24rem; }
        ul.start-here-list li { min-width: 0; }
        ul.start-here-list code { display: inline-block; max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
                                  font-size: .78rem; padding: .14rem .42rem; background: #f1f5f9; border: 1px solid #e2e8f0; border-radius: 5px; color: #334155; }
        details.portal-section { background: #ffffff; border: 1px solid #dce1e8; border-radius: 10px; margin: 1rem 0; overflow: hidden; }
        summary.portal-summary { cursor: pointer; padding: .75rem 1rem; font-weight: 600; font-size: .96rem;
                                 list-style: none; display: flex; align-items: center; gap: .5rem; background: #f8fafc; }
        summary.portal-summary::-webkit-details-marker { display: none; }
        summary.portal-summary::before { content: "▸"; color: #64748b; font-size: .85rem; }
        details.portal-section[open] > summary.portal-summary::before { content: "▾"; }
        .portal-body { padding: .95rem 1.05rem 1.1rem; border-top: 1px solid #e5e7eb; overflow-x: auto; }
        .portal-body > h2:first-child { margin-top: .25rem; }
        h1 { font-size: 1.8rem; margin: 0 0 .25rem; line-height: 1.25; }
        h2 { font-size: 1.25rem; margin: 2rem 0 .75rem; border-bottom: 1px solid #dce1e8; padding-bottom: .35rem; }
        h3 { font-size: 1.05rem; margin: 1.4rem 0 .5rem; }
        h4 { font-size: .88rem; text-transform: uppercase; letter-spacing: .04em; color: #64748b; margin: 0 0 .5rem; }
        .meta, .footer, .note { color: #5a6068; font-size: .9rem; }
        .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: .8rem; margin: 1.25rem 0 .75rem; }
        .card { background: #ffffff; border: 1px solid #dce1e8; border-radius: 10px; padding: .95rem 1rem; text-align: center;
                box-shadow: 0 1px 2px rgba(15, 23, 42, .04); display: flex; flex-direction: column; justify-content: center; min-width: 0; }
        .card-value { font-size: 1.8rem; font-weight: 700; line-height: 1.15; }
        .card-value.card-value-text { font-size: 1.02rem; font-weight: 650; line-height: 1.35; overflow-wrap: anywhere; }
        .card-label { color: #5a6068; font-size: .84rem; margin-top: .28rem; }
        figure.diagram { margin: 0 0 1.4rem; }
        figure.diagram figcaption { color: #5a6068; font-size: .85rem; margin-top: .5rem; }
        .svg-diagram { background: #ffffff; border: 1px solid #dce1e8; border-radius: 12px; padding: 1.25rem;
                       overflow: auto; max-height: 78vh; box-shadow: 0 1px 3px rgba(15, 23, 42, .04); }
        .svg-diagram svg { display: block; width: auto; height: auto; max-width: none; margin: 0 auto; }
        .svg-diagram.is-interactive { cursor: grab; user-select: none; }
        .svg-diagram.is-interactive.is-dragging { cursor: grabbing; }
        .svg-diagram.is-interactive .node, .svg-diagram.is-interactive .edge, .svg-diagram.is-interactive .edge-label,
        .svg-diagram.is-interactive .er-entity, .svg-diagram.is-interactive .er-relationship,
        .svg-diagram.is-interactive .er-relationship-label, .svg-diagram.is-interactive .seq-participant,
        .svg-diagram.is-interactive .seq-message { cursor: pointer; }
        .svg-diagram .node, .svg-diagram .edge, .svg-diagram .edge-label, .svg-diagram .subgraph,
        .svg-diagram .er-entity, .svg-diagram .er-relationship, .svg-diagram .er-relationship-label,
        .svg-diagram .seq-participant, .svg-diagram .seq-message, .svg-diagram .svg-seq-lifeline, .svg-diagram .seq-note {
          transition: opacity .15s ease;
        }
        .svg-diagram .node:hover .svg-node-box,
        .svg-diagram .er-entity:hover .svg-er-box,
        .svg-diagram .seq-participant:hover .svg-seq-box { stroke-width: 2.3; filter: drop-shadow(0 2px 5px rgba(37, 99, 235, .25)); }
        .svg-diagram .edge:hover .svg-edge-path,
        .svg-diagram .er-relationship:hover .svg-er-line,
        .svg-diagram .seq-message:hover .svg-seq-msg-line { stroke: #2563eb; stroke-width: 2.5; }
        .diagram-toolbar { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: .5rem;
                           margin-bottom: .45rem; }
        .diagram-search { padding: .34rem .65rem; border: 1px solid #cbd5e1; border-radius: 7px; font-size: .84rem;
                          min-width: 190px; max-width: 280px; width: 100%; background: #ffffff; color: inherit; }
        .diagram-mode-group, .diagram-zoom-group { display: flex; flex-wrap: wrap; align-items: center; gap: .28rem; }
        .diagram-btn { cursor: pointer; padding: .26rem .56rem; border: 1px solid #cbd5e1; border-radius: 6px;
                       background: #ffffff; color: #334155; font-size: .78rem; font-weight: 600; }
        .diagram-btn:hover { background: #f1f5f9; }
        .diagram-btn.is-active { background: #2563eb; border-color: #2563eb; color: #ffffff; }
        .diagram-inspector { background: #f8fafc; border: 1px solid #dce1e8; border-radius: 9px; padding: .52rem .75rem;
                             margin-bottom: .5rem; font-size: .83rem; display: flex; flex-direction: column; gap: .38rem; }
        .inspector-hint { color: #64748b; font-size: .8rem; }
        .inspector-header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: .5rem; }
        .inspector-header-left, .inspector-header-right, .inspector-edge-flow { display: flex; flex-wrap: wrap; align-items: center; gap: .45rem; }
        .inspector-title { font-weight: 700; color: #0f172a; }
        .inspector-stats { font-size: .76rem; padding: .12rem .48rem; border-radius: 999px; background: #e2e8f0; color: #334155; font-weight: 600; }
        .inspector-clear { cursor: pointer; padding: .18rem .52rem; border: 1px solid #cbd5e1; border-radius: 6px;
                           background: #ffffff; color: #334155; font-size: .76rem; font-weight: 600; }
        .inspector-clear:hover { background: #f1f5f9; }
        .inspector-group { display: flex; flex-wrap: wrap; align-items: center; gap: .32rem; }
        .inspector-group-label { font-size: .74rem; font-weight: 700; text-transform: uppercase; letter-spacing: .04em; margin-right: .15rem; }
        .chip-in-lbl { color: #0369a1; }
        .chip-out-lbl { color: #b45309; }
        .inspector-chip { cursor: pointer; padding: .16rem .52rem; border-radius: 999px; font-size: .77rem; font-weight: 600;
                          border: 1px solid #cbd5e1; background: #ffffff; color: #1e293b; }
        .inspector-chip.chip-in { background: #f0f9ff; border-color: #7dd3fc; color: #0369a1; }
        .inspector-chip.chip-in:hover { background: #e0f2fe; }
        .inspector-chip.chip-out { background: #fffbeb; border-color: #fcd34d; color: #b45309; }
        .inspector-chip.chip-out:hover { background: #fef3c7; }
        .inspector-chip.chip-match { background: #eff6ff; border-color: #93c5fd; color: #1d4ed8; }
        .inspector-arrow { color: #475569; font-weight: 600; }
        .svg-diagram svg.is-focusing .node.is-dim,
        .svg-diagram svg.is-focusing .edge.is-dim,
        .svg-diagram svg.is-focusing .edge-label.is-dim,
        .svg-diagram svg.is-focusing .er-entity.is-dim,
        .svg-diagram svg.is-focusing .er-relationship.is-dim,
        .svg-diagram svg.is-focusing .er-relationship-label.is-dim,
        .svg-diagram svg.is-focusing .seq-participant.is-dim,
        .svg-diagram svg.is-focusing .seq-message.is-dim,
        .svg-diagram svg.is-focusing .seq-note.is-dim,
        .svg-diagram svg.is-focusing .svg-seq-lifeline.is-dim,
        pre.mermaid svg.is-focusing g.node.is-dim,
        pre.mermaid svg.is-focusing path.flowchart-link.is-dim { opacity: 0.13; }
        .svg-diagram svg.is-focusing .subgraph.is-dim { opacity: 0.30; }
        .svg-diagram svg.is-focusing .node.is-selected .svg-node-box,
        .svg-diagram svg.is-focusing .er-entity.is-selected .svg-er-box,
        .svg-diagram svg.is-focusing .seq-participant.is-selected .svg-seq-box { stroke: #2563eb; stroke-width: 2.8; filter: drop-shadow(0 0 6px rgba(37, 99, 235, .45)); }
        .svg-diagram svg.is-focusing .node.is-selected .svg-node-text,
        .svg-diagram svg.is-focusing .er-entity.is-selected .svg-er-title,
        .svg-diagram svg.is-focusing .seq-participant.is-selected .svg-seq-text { font-weight: 700; }
        .svg-diagram svg.is-focusing .node.is-neighbor-in .svg-node-box,
        .svg-diagram svg.is-focusing .er-entity.is-neighbor-in .svg-er-box,
        .svg-diagram svg.is-focusing .seq-participant.is-neighbor-in .svg-seq-box { stroke: #0284c7; stroke-width: 2.3; }
        .svg-diagram svg.is-focusing .node.is-neighbor-out .svg-node-box,
        .svg-diagram svg.is-focusing .er-entity.is-neighbor-out .svg-er-box,
        .svg-diagram svg.is-focusing .seq-participant.is-neighbor-out .svg-seq-box { stroke: #d97706; stroke-width: 2.3; }
        .svg-diagram svg.is-focusing .node.is-neighbor-both .svg-node-box,
        .svg-diagram svg.is-focusing .er-entity.is-neighbor-both .svg-er-box,
        .svg-diagram svg.is-focusing .seq-participant.is-neighbor-both .svg-seq-box { stroke: #7c3aed; stroke-width: 2.4; }
        .svg-diagram svg.is-focusing .node.is-match .svg-node-box,
        .svg-diagram svg.is-focusing .er-entity.is-match .svg-er-box,
        .svg-diagram svg.is-focusing .seq-participant.is-match .svg-seq-box { stroke: #2563eb; stroke-width: 2.5; filter: drop-shadow(0 0 5px rgba(37, 99, 235, .35)); }
        .svg-diagram svg.is-focusing .edge.is-in .svg-edge-path,
        .svg-diagram svg.is-focusing .er-relationship.is-in .svg-er-line,
        .svg-diagram svg.is-focusing .seq-message.is-in .svg-seq-msg-line { stroke: #0284c7; stroke-width: 2.5; }
        .svg-diagram svg.is-focusing .edge.is-out .svg-edge-path,
        .svg-diagram svg.is-focusing .er-relationship.is-out .svg-er-line,
        .svg-diagram svg.is-focusing .seq-message.is-out .svg-seq-msg-line { stroke: #d97706; stroke-width: 2.5; }
        .svg-diagram svg.is-focusing .edge.is-active .svg-edge-path,
        .svg-diagram svg.is-focusing .er-relationship.is-active .svg-er-line,
        .svg-diagram svg.is-focusing .seq-message.is-active .svg-seq-msg-line,
        pre.mermaid svg.is-focusing path.flowchart-link.is-active { stroke: #2563eb; stroke-width: 2.8; }
        .svg-diagram svg.is-focusing .edge-label.is-in .svg-edge-pill,
        .svg-diagram svg.is-focusing .er-relationship-label.is-in .svg-er-pill,
        .svg-diagram svg.is-focusing .seq-message.is-in .svg-seq-msg-bg { fill: #e0f2fe; stroke: #0284c7; stroke-width: 1.5; }
        .svg-diagram svg.is-focusing .edge-label.is-in .svg-edge-text,
        .svg-diagram svg.is-focusing .er-relationship-label.is-in .svg-er-pill-text,
        .svg-diagram svg.is-focusing .seq-message.is-in .svg-seq-msg-text { fill: #0c4a6e; font-weight: 600; }
        .svg-diagram svg.is-focusing .edge-label.is-out .svg-edge-pill,
        .svg-diagram svg.is-focusing .er-relationship-label.is-out .svg-er-pill,
        .svg-diagram svg.is-focusing .seq-message.is-out .svg-seq-msg-bg { fill: #fef3c7; stroke: #d97706; stroke-width: 1.5; }
        .svg-diagram svg.is-focusing .edge-label.is-out .svg-edge-text,
        .svg-diagram svg.is-focusing .er-relationship-label.is-out .svg-er-pill-text,
        .svg-diagram svg.is-focusing .seq-message.is-out .svg-seq-msg-text { fill: #78350f; font-weight: 600; }
        .svg-diagram svg.is-focusing .edge-label.is-active .svg-edge-pill,
        .svg-diagram svg.is-focusing .er-relationship-label.is-active .svg-er-pill,
        .svg-diagram svg.is-focusing .seq-message.is-active .svg-seq-msg-bg { fill: #dbeafe; stroke: #2563eb; stroke-width: 1.6; }
        .svg-diagram svg.is-focusing .edge-label.is-active .svg-edge-text,
        .svg-diagram svg.is-focusing .er-relationship-label.is-active .svg-er-pill-text,
        .svg-diagram svg.is-focusing .seq-message.is-active .svg-seq-msg-text { fill: #1e3a8a; font-weight: 600; }
        .svg-diagram svg.is-focusing .svg-seq-lifeline.is-active { stroke: #4f46e5; stroke-width: 1.8; }
        pre.mermaid { background: #ffffff; border: 1px solid #dce1e8; border-radius: 12px; padding: 1.25rem;
                      overflow: auto; max-height: 78vh; box-shadow: 0 1px 3px rgba(15, 23, 42, .04); }
        .mermaid-toolbar { display: flex; align-items: center; gap: .35rem; margin-bottom: .45rem; justify-content: flex-end; }
        .mermaid-zoom-btn { cursor: pointer; padding: .22rem .55rem; border: 1px solid #cbd5e1; border-radius: 6px;
                            background: #ffffff; color: #334155; font-size: .78rem; font-weight: 600; }
        .mermaid-zoom-btn:hover { background: #f1f5f9; }
        ul.legend { display: flex; flex-wrap: wrap; gap: .4rem 1.05rem; list-style: none; padding: 0; margin: .5rem 0 1rem; font-size: .88rem; }
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
        th, td { border: 1px solid #dce1e8; padding: .5rem .65rem; text-align: left; font-size: .92rem; }
        th { background: #f1f5f9; }
        a.req-link { font-weight: 600; text-decoration: none; }
        a.req-link:hover { text-decoration: underline; }
        .cqrs-explorer { margin: .75rem 0 1.5rem; }
        .filter-radio { position: absolute; opacity: 0; pointer-events: none; }
        .explorer-toolbar { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: .75rem; margin-bottom: .85rem; }
        .filter-pills { display: flex; flex-wrap: wrap; gap: .4rem; }
        .filter-pill { cursor: pointer; padding: .35rem .75rem; border-radius: 999px; font-size: .85rem; font-weight: 600; background: #ffffff; border: 1px solid #dce1e8; color: #334155; user-select: none; }
        #cqrs-filter-all:checked ~ .explorer-toolbar label[for="cqrs-filter-all"],
        #cqrs-filter-command:checked ~ .explorer-toolbar label[for="cqrs-filter-command"],
        #cqrs-filter-query:checked ~ .explorer-toolbar label[for="cqrs-filter-query"],
        #cqrs-filter-issues:checked ~ .explorer-toolbar label[for="cqrs-filter-issues"] { background: #2563eb; border-color: #2563eb; color: #ffffff; }
        #cqrs-filter-command:checked ~ .explorer-list details.explorer-item:not([data-kind="command"]) { display: none; }
        #cqrs-filter-query:checked ~ .explorer-list details.explorer-item:not([data-kind="query"]) { display: none; }
        #cqrs-filter-issues:checked ~ .explorer-list details.explorer-item:not([data-issues="true"]) { display: none; }
        .explorer-actions { display: flex; flex-wrap: wrap; align-items: center; gap: .45rem; flex: 1; justify-content: flex-end; }
        .explorer-search { padding: .4rem .7rem; border: 1px solid #dce1e8; border-radius: 8px; font-size: .88rem; min-width: 240px; max-width: 340px; width: 100%; background: #ffffff; color: inherit; }
        .explorer-btn { cursor: pointer; padding: .38rem .68rem; border: 1px solid #dce1e8; border-radius: 8px; background: #ffffff; color: inherit; font-size: .84rem; font-weight: 500; }
        .explorer-btn:hover { background: #eceef1; }
        details.explorer-item { background: #ffffff; border: 1px solid #dce1e8; border-radius: 10px; margin: 0 0 .55rem; overflow: hidden; transition: border-color .15s, box-shadow .15s; }
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
        ul.checks li { padding: .45rem .65rem; border-radius: 6px; margin-bottom: .35rem; }
        ul.checks li.error { background: #fdecec; }
        ul.checks li.warning { background: #fff6db; }
        ul.checks li.info { background: #e8f1ff; }
        code { background: #eceef1; padding: .1rem .32rem; border-radius: 4px; }
        hr { border: none; border-top: 1px solid #dce1e8; margin: 2rem 0 1rem; }
        a { color: #0b5fff; }
        .watermark-overlay { position: fixed; inset: 0; pointer-events: none; z-index: 9999; display: flex; align-items: center; justify-content: center; font-size: clamp(2.5rem, 6vw, 5.5rem); font-weight: 900; letter-spacing: .18em; text-transform: uppercase; color: rgba(99, 102, 241, .06); transform: rotate(-24deg); user-select: none; white-space: nowrap; }
        pre.ascii-watermark { background: linear-gradient(135deg, #0f172a 0%, #1e1b4b 100%); color: #38bdf8; border: 1px solid #312e81; border-radius: 10px; padding: 1rem 1.25rem; margin: 1rem 0; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: .78rem; line-height: 1.15; overflow-x: auto; text-align: center; box-shadow: 0 4px 14px rgba(15, 23, 42, .12); }
        @media (prefers-color-scheme: dark) {
          body { background: #0f172a; color: #e2e8f0; }
          .card, table, details.explorer-item, .explorer-panel, .filter-pill, .explorer-search, .explorer-btn, nav.toc, .service-card, details.portal-section { background: #161e2e; border-color: #273449; color: #e2e8f0; }
          summary.portal-summary { background: #1e293b; color: #e2e8f0; }
          .portal-body { border-color: #273449; }
          nav.toc a:hover { background: #1e293b; }
          .toc-title, .start-here, .start-here-title { color: #94a3b8; }
          ul.start-here-list code { background: #1e293b; border-color: #334155; color: #cbd5e1; }
          .tech-badge { background: #1e293b; color: #c7d2fe; border-color: #4338ca; }
          .role-api { background: #1e3a8a; color: #dbeafe; border-color: #3b82f6; }
          .role-worker { background: #7c2d12; color: #ffedd5; border-color: #f97316; }
          .role-ui { background: #701a75; color: #fae8ff; border-color: #d946ef; }
          .role-orchestrator { background: #4c1d95; color: #ede9fe; border-color: #8b5cf6; }
          .role-library, .role-tests { background: #1e293b; color: #cbd5e1; border-color: #475569; }
          .explorer-body { background: #131b29; border-color: #273449; }
          .meta-pill { background: #1e293b; color: #cbd5e1; }
          .badge-command { background: #1e3a8a; color: #dbeafe; border-color: #3b82f6; }
          .badge-query { background: #134e4a; color: #ccfbf1; border-color: #14b8a6; }
          .badge-request { background: #4c1d95; color: #ede9fe; border-color: #8b5cf6; }
          th { background: #1e293b; }
          th, td { border-color: #273449; }
          h2 { border-color: #273449; }
          code { background: #1e293b; }
          ul.checks li.error { background: #3a1a22; }
          ul.checks li.warning { background: #383016; }
          ul.checks li.info { background: #172554; }
          .meta, .footer, .note, .card-label, figure.diagram figcaption, .explorer-response, .meta-sub, dl.explorer-dl dt, h4 { color: #94a3b8; }
          .diagram-error { background: #3a1a22; border-color: #7f1d1d; color: #fecaca; }
          details pre { background: #1e293b; }
          hr { border-color: #273449; }
          a { color: #60a5fa; }
          .mermaid-zoom-btn { background: #1e293b; border-color: #334155; color: #e2e8f0; }
          .mermaid-zoom-btn:hover { background: #334155; }
          pre.mermaid { background: #f1f5f9; border-color: #334155; color: #0f172a; }
          .svg-diagram { background: #131b29; border-color: #273449; }
          .svg-sg-box { fill: #1b2538; fill-opacity: .72; stroke: #475569; }
          .svg-sg-divider { stroke: #273449; }
          .svg-sg-label { fill: #cbd5e1; }
          .svg-edge-path { stroke: #94a3b8; }
          .svg-edge-path.is-violation { stroke: #f87171; }
          .svg-arrow-default { fill: #94a3b8; }
          .svg-arrow-violation { fill: #f87171; }
          .svg-edge-pill { fill: #1e293b; fill-opacity: .96; stroke: #475569; }
          .svg-edge-text { fill: #e2e8f0; }
          .edge-label.is-violation .svg-edge-pill { fill: #450a0a; stroke: #ef4444; }
          .edge-label.is-violation .svg-edge-text { fill: #fecaca; }
          .svg-node-box { fill: #1e293b; stroke: #64748b; }
          .svg-node-text { fill: #f1f5f9; }
          .svg-node-box[data-role="clsUi"] { fill: #2e1065; stroke: #c084fc; }
          .svg-node-text[data-role="clsUi"] { fill: #f5d0fe; }
          .svg-node-box[data-role="clsApi"],
          .svg-node-box[data-role="clsRequest"],
          .svg-node-box[data-role="clsCommand"],
          .svg-node-box[data-role="clsFlowRequest"],
          .svg-node-box[data-role="clsApplication"],
          .svg-node-box[data-role="clsDirectGrpc"] { fill: #172554; stroke: #60a5fa; }
          .svg-node-text[data-role="clsApi"],
          .svg-node-text[data-role="clsRequest"],
          .svg-node-text[data-role="clsCommand"],
          .svg-node-text[data-role="clsFlowRequest"],
          .svg-node-text[data-role="clsApplication"],
          .svg-node-text[data-role="clsDirectGrpc"] { fill: #dbeafe; }
          .svg-node-box[data-role="clsQuery"] { fill: #134e4a; stroke: #2dd4bf; }
          .svg-node-text[data-role="clsQuery"] { fill: #ccfbf1; }
          .svg-node-box[data-role="clsFlowEndpoint"],
          .svg-node-box[data-role="clsDirectEndpoint"] { fill: #0c4a6e; stroke: #38bdf8; }
          .svg-node-text[data-role="clsFlowEndpoint"],
          .svg-node-text[data-role="clsDirectEndpoint"] { fill: #e0f2fe; }
          .svg-node-box[data-role="clsWorker"],
          .svg-node-box[data-role="clsValidator"],
          .svg-node-box[data-role="clsInfrastructure"],
          .svg-node-box[data-role="clsBehavior"],
          .svg-node-box[data-role="clsDirectWorker"] { fill: #431407; stroke: #fb923c; }
          .svg-node-text[data-role="clsWorker"],
          .svg-node-text[data-role="clsValidator"],
          .svg-node-text[data-role="clsInfrastructure"],
          .svg-node-text[data-role="clsBehavior"],
          .svg-node-text[data-role="clsDirectWorker"] { fill: #ffedd5; }
          .svg-node-box[data-role="clsOrchestrator"] { fill: #2e1065; stroke: #a78bfa; }
          .svg-node-text[data-role="clsOrchestrator"] { fill: #ede9fe; }
          .svg-node-box[data-role="clsData"],
          .svg-node-box[data-role="clsDomain"],
          .svg-node-box[data-role="clsFlowDbContext"],
          .svg-node-box[data-role="clsDirectDbContext"],
          .svg-node-box[data-role="clsImpDownstream"],
          .svg-node-box[data-role="clsImpTested"] { fill: #064e3b; stroke: #34d399; }
          .svg-node-text[data-role="clsData"],
          .svg-node-text[data-role="clsDomain"],
          .svg-node-text[data-role="clsFlowDbContext"],
          .svg-node-text[data-role="clsDirectDbContext"],
          .svg-node-text[data-role="clsImpDownstream"],
          .svg-node-text[data-role="clsImpTested"] { fill: #d1fae5; }
          .svg-node-box[data-role="clsBroker"],
          .svg-node-box[data-role="clsDirectEvent"] { fill: #422006; stroke: #facc15; }
          .svg-node-text[data-role="clsBroker"],
          .svg-node-text[data-role="clsDirectEvent"] { fill: #fef9c3; }
          .svg-node-box[data-role="clsDirectConsumer"],
          .svg-node-box[data-role="clsFlowNotification"] { fill: #4a044e; stroke: #e879f9; }
          .svg-node-text[data-role="clsDirectConsumer"],
          .svg-node-text[data-role="clsFlowNotification"] { fill: #fae8ff; }
          .svg-node-box[data-role="clsImpHotspot"],
          .svg-node-box[data-role="clsMissing"] { fill: #450a0a; stroke: #f87171; }
          .svg-node-text[data-role="clsImpHotspot"],
          .svg-node-text[data-role="clsMissing"] { fill: #fecaca; }
          .svg-node-box[data-role="clsNoValidator"] { fill: #172554; stroke: #fbbf24; }
          .svg-node-text[data-role="clsNoValidator"] { fill: #dbeafe; }
          .svg-node-box[data-role="clsImpTarget"],
          .svg-node-box[data-role="clsFocus"] { fill: #1e3a8a; stroke: #93c5fd; }
          .svg-node-text[data-role="clsImpTarget"],
          .svg-node-text[data-role="clsFocus"] { fill: #eff6ff; }
          .svg-node-box[data-role="clsHandler"],
          .svg-node-box[data-role="clsFlowHandler"],
          .svg-node-box[data-role="clsDirectService"],
          .svg-node-box[data-role="clsImpUpstream"] { fill: #1e293b; stroke: #94a3b8; }
          .svg-node-text[data-role="clsHandler"],
          .svg-node-text[data-role="clsFlowHandler"],
          .svg-node-text[data-role="clsDirectService"],
          .svg-node-text[data-role="clsImpUpstream"] { fill: #e2e8f0; }
          .svg-seq-lifeline { stroke: #475569; }
          .svg-seq-box { fill: #1e1b4b; stroke: #818cf8; }
          .svg-seq-text { fill: #e0e7ff; }
          .svg-seq-msg-line { stroke: #94a3b8; }
          .svg-seq-msg-bg { fill: #1e293b; stroke: #475569; }
          .svg-seq-msg-text { fill: #e2e8f0; }
          .svg-seq-note-box { fill: #422006; stroke: #eab308; }
          .svg-seq-note-text { fill: #fef08a; }
          .svg-er-line { stroke: #94a3b8; }
          .svg-er-box { fill: #161e2e; stroke: #60a5fa; }
          .svg-er-header { fill: #1e3a8a; stroke: #60a5fa; }
          .svg-er-title { fill: #dbeafe; }
          .svg-er-sep { stroke: #273449; }
          .svg-er-attr { fill: #cbd5e1; }
          .svg-er-key { fill: #fbbf24; }
          .svg-er-pill { fill: #1e293b; stroke: #475569; }
          .svg-er-pill-text { fill: #e2e8f0; }
          .diagram-search, .diagram-btn, .inspector-clear { background: #1e293b; border-color: #334155; color: #e2e8f0; }
          .diagram-btn:hover, .inspector-clear:hover { background: #334155; }
          .diagram-btn.is-active { background: #2563eb; border-color: #3b82f6; color: #ffffff; }
          .diagram-inspector { background: #161e2e; border-color: #273449; color: #e2e8f0; }
          .inspector-title { color: #f8fafc; }
          .inspector-stats { background: #1e293b; color: #cbd5e1; }
          .inspector-hint, .inspector-arrow { color: #94a3b8; }
          .chip-in-lbl { color: #38bdf8; }
          .chip-out-lbl { color: #fbbf24; }
          .inspector-chip.chip-in { background: #0c4a6e; border-color: #0284c7; color: #e0f2fe; }
          .inspector-chip.chip-in:hover { background: #075985; }
          .inspector-chip.chip-out { background: #451a03; border-color: #d97706; color: #fef3c7; }
          .inspector-chip.chip-out:hover { background: #78350f; }
          .inspector-chip.chip-match { background: #172554; border-color: #3b82f6; color: #dbeafe; }
          .svg-diagram .edge:hover .svg-edge-path,
          .svg-diagram .er-relationship:hover .svg-er-line,
          .svg-diagram .seq-message:hover .svg-seq-msg-line { stroke: #60a5fa; }
          .svg-arrow-in { fill: #38bdf8; }
          .svg-arrow-out { fill: #fbbf24; }
          .svg-arrow-active { fill: #60a5fa; }
          .svg-diagram svg.is-focusing .node.is-selected .svg-node-box,
          .svg-diagram svg.is-focusing .er-entity.is-selected .svg-er-box,
          .svg-diagram svg.is-focusing .seq-participant.is-selected .svg-seq-box { stroke: #60a5fa; stroke-width: 2.8; filter: drop-shadow(0 0 7px rgba(96, 165, 250, .55)); }
          .svg-diagram svg.is-focusing .node.is-neighbor-in .svg-node-box,
          .svg-diagram svg.is-focusing .er-entity.is-neighbor-in .svg-er-box,
          .svg-diagram svg.is-focusing .seq-participant.is-neighbor-in .svg-seq-box { stroke: #38bdf8; stroke-width: 2.3; }
          .svg-diagram svg.is-focusing .node.is-neighbor-out .svg-node-box,
          .svg-diagram svg.is-focusing .er-entity.is-neighbor-out .svg-er-box,
          .svg-diagram svg.is-focusing .seq-participant.is-neighbor-out .svg-seq-box { stroke: #fbbf24; stroke-width: 2.3; }
          .svg-diagram svg.is-focusing .node.is-neighbor-both .svg-node-box,
          .svg-diagram svg.is-focusing .er-entity.is-neighbor-both .svg-er-box,
          .svg-diagram svg.is-focusing .seq-participant.is-neighbor-both .svg-seq-box { stroke: #c084fc; stroke-width: 2.4; }
          .svg-diagram svg.is-focusing .node.is-match .svg-node-box,
          .svg-diagram svg.is-focusing .er-entity.is-match .svg-er-box,
          .svg-diagram svg.is-focusing .seq-participant.is-match .svg-seq-box { stroke: #60a5fa; stroke-width: 2.5; filter: drop-shadow(0 0 6px rgba(96, 165, 250, .45)); }
          .svg-diagram svg.is-focusing .edge.is-in .svg-edge-path,
          .svg-diagram svg.is-focusing .er-relationship.is-in .svg-er-line,
          .svg-diagram svg.is-focusing .seq-message.is-in .svg-seq-msg-line { stroke: #38bdf8; stroke-width: 2.5; }
          .svg-diagram svg.is-focusing .edge.is-out .svg-edge-path,
          .svg-diagram svg.is-focusing .er-relationship.is-out .svg-er-line,
          .svg-diagram svg.is-focusing .seq-message.is-out .svg-seq-msg-line { stroke: #fbbf24; stroke-width: 2.5; }
          .svg-diagram svg.is-focusing .edge.is-active .svg-edge-path,
          .svg-diagram svg.is-focusing .er-relationship.is-active .svg-er-line,
          .svg-diagram svg.is-focusing .seq-message.is-active .svg-seq-msg-line { stroke: #60a5fa; stroke-width: 2.8; }
          .svg-diagram svg.is-focusing .edge-label.is-in .svg-edge-pill,
          .svg-diagram svg.is-focusing .er-relationship-label.is-in .svg-er-pill,
          .svg-diagram svg.is-focusing .seq-message.is-in .svg-seq-msg-bg { fill: #0c4a6e; stroke: #38bdf8; }
          .svg-diagram svg.is-focusing .edge-label.is-in .svg-edge-text,
          .svg-diagram svg.is-focusing .er-relationship-label.is-in .svg-er-pill-text,
          .svg-diagram svg.is-focusing .seq-message.is-in .svg-seq-msg-text { fill: #e0f2fe; }
          .svg-diagram svg.is-focusing .edge-label.is-out .svg-edge-pill,
          .svg-diagram svg.is-focusing .er-relationship-label.is-out .svg-er-pill,
          .svg-diagram svg.is-focusing .seq-message.is-out .svg-seq-msg-bg { fill: #451a03; stroke: #fbbf24; }
          .svg-diagram svg.is-focusing .edge-label.is-out .svg-edge-text,
          .svg-diagram svg.is-focusing .er-relationship-label.is-out .svg-er-pill-text,
          .svg-diagram svg.is-focusing .seq-message.is-out .svg-seq-msg-text { fill: #fef3c7; }
          .svg-diagram svg.is-focusing .edge-label.is-active .svg-edge-pill,
          .svg-diagram svg.is-focusing .er-relationship-label.is-active .svg-er-pill,
          .svg-diagram svg.is-focusing .seq-message.is-active .svg-seq-msg-bg { fill: #172554; stroke: #60a5fa; }
          .svg-diagram svg.is-focusing .edge-label.is-active .svg-edge-text,
          .svg-diagram svg.is-focusing .er-relationship-label.is-active .svg-er-pill-text,
          .svg-diagram svg.is-focusing .seq-message.is-active .svg-seq-msg-text { fill: #dbeafe; }
          .svg-diagram svg.is-focusing .svg-seq-lifeline.is-active { stroke: #818cf8; stroke-width: 1.8; }
        }
        """;
}
