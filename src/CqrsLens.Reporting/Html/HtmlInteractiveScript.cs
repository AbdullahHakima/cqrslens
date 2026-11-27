namespace CqrsLens;

/// <summary>Embedded zero-dependency vanilla JS interactive SVG diagram explorer.</summary>
internal static class HtmlInteractiveScript
{
    internal const string Script = """
                (() => {
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

                  const byName = new Map();
                  explorerItems.forEach((item) => {
                    const reqName = item.getAttribute('data-request-name');
                    const reqFull = item.getAttribute('data-request-fullname');
                    if (reqName && !byName.has(reqName)) byName.set(reqName, item.id);
                    if (reqFull) byName.set(reqFull, item.id);
                  });

                  const mkEl = (tag, cls, text) => {
                    const el = document.createElement(tag);
                    if (cls) el.className = cls;
                    if (text !== undefined) el.textContent = text;
                    return el;
                  };

                  const numAttr = (el, name) => parseFloat(el.getAttribute(name) || '0') || 0;

                  const enhanceSvgDiagram = (container) => {
                    const svg = container.querySelector('svg');
                    if (!svg || container.dataset.interactiveAttached === '1') return;
                    container.dataset.interactiveAttached = '1';

                    const flowchartNodes = [...svg.querySelectorAll('g.node[data-id]')];
                    const erNodes = [...svg.querySelectorAll('g.er-entity[data-id]')];
                    const seqNodes = [...svg.querySelectorAll('g.seq-participant[data-id]')];

                    let kind = 'flowchart';
                    let rawNodeEls = flowchartNodes;
                    if (erNodes.length > 0) {
                      kind = 'er';
                      rawNodeEls = erNodes;
                    } else if (seqNodes.length > 0) {
                      kind = 'sequence';
                      rawNodeEls = seqNodes;
                    }

                    const nodeGroups = new Map();
                    const nodeLabels = new Map();
                    rawNodeEls.forEach((el) => {
                      const id = el.getAttribute('data-id');
                      if (!id) return;
                      if (!nodeGroups.has(id)) nodeGroups.set(id, []);
                      nodeGroups.get(id).push(el);
                      if (!nodeLabels.has(id)) {
                        const txtEl = kind === 'er'
                          ? el.querySelector('.svg-er-title')
                          : kind === 'sequence'
                            ? el.querySelector('.svg-seq-text')
                            : el.querySelector('.svg-node-text, text');
                        nodeLabels.set(id, ((txtEl && txtEl.textContent) || id).trim());
                      }
                      el.setAttribute('tabindex', '0');
                      el.setAttribute('role', 'button');
                      el.setAttribute('aria-label', nodeLabels.get(id));
                    });

                    const edges = [];
                    const origMarkers = new Map();
                    const pillsByIdx = new Map();
                    const lifelinesById = new Map();
                    const seqNotes = [...svg.querySelectorAll('g.seq-note')];
                    const subgraphs = [];

                    if (kind === 'flowchart') {
                      svg.querySelectorAll('g.edge-label[data-edge-index]').forEach((p) => {
                        pillsByIdx.set(p.getAttribute('data-edge-index'), p);
                      });
                      [...svg.querySelectorAll('g.edge[data-source][data-target]')].forEach((el, i) => {
                        const key = el.getAttribute('data-edge-index') || String(i);
                        const pathEl = el.querySelector('path.svg-edge-path');
                        if (pathEl && !origMarkers.has(pathEl))
                          origMarkers.set(pathEl, pathEl.getAttribute('marker-end') || '');
                        edges.push({
                          key,
                          el,
                          pill: pillsByIdx.get(key) || null,
                          src: el.getAttribute('data-source') || '',
                          dst: el.getAttribute('data-target') || '',
                          label: (el.getAttribute('data-label') || (el.querySelector('title') && el.querySelector('title').textContent) || '').trim(),
                          lineEl: pathEl
                        });
                      });
                      svg.querySelectorAll('g.subgraph').forEach((sgEl) => {
                        const rect = sgEl.querySelector('rect.svg-sg-box');
                        if (!rect) return;
                        const sx = numAttr(rect, 'x');
                        const sy = numAttr(rect, 'y');
                        const sw = numAttr(rect, 'width');
                        const sh = numAttr(rect, 'height');
                        const members = new Set();
                        nodeGroups.forEach((els, id) => {
                          const r = els[0] && els[0].querySelector('rect');
                          if (!r) return;
                          const cx = numAttr(r, 'x') + numAttr(r, 'width') / 2;
                          const cy = numAttr(r, 'y') + numAttr(r, 'height') / 2;
                          if (cx >= sx && cx <= sx + sw && cy >= sy && cy <= sy + sh) members.add(id);
                        });
                        subgraphs.push({ el: sgEl, members });
                      });
                    } else if (kind === 'er') {
                      svg.querySelectorAll('g.er-relationship-label[data-edge-index]').forEach((p) => {
                        pillsByIdx.set(p.getAttribute('data-edge-index'), p);
                      });
                      [...svg.querySelectorAll('g.er-relationship[data-principal][data-dependent]')].forEach((el, i) => {
                        const key = el.getAttribute('data-edge-index') || String(i);
                        const lineEl = el.querySelector('path.svg-er-line');
                        if (lineEl && !origMarkers.has(lineEl))
                          origMarkers.set(lineEl, lineEl.getAttribute('marker-end') || '');
                        edges.push({
                          key,
                          el,
                          pill: pillsByIdx.get(key) || null,
                          src: el.getAttribute('data-principal') || '',
                          dst: el.getAttribute('data-dependent') || '',
                          label: (el.getAttribute('data-label') || '').trim(),
                          lineEl
                        });
                      });
                    } else {
                      svg.querySelectorAll('line.svg-seq-lifeline[data-participant]').forEach((lineEl) => {
                        const pid = lineEl.getAttribute('data-participant');
                        if (pid) lifelinesById.set(pid, lineEl);
                      });
                      [...svg.querySelectorAll('g.seq-message[data-from][data-to]')].forEach((el, i) => {
                        const key = el.getAttribute('data-edge-index') || String(i);
                        const lineEl = el.querySelector('.svg-seq-msg-line');
                        if (lineEl && !origMarkers.has(lineEl))
                          origMarkers.set(lineEl, lineEl.getAttribute('marker-end') || '');
                        edges.push({
                          key,
                          el,
                          pill: null,
                          src: el.getAttribute('data-from') || '',
                          dst: el.getAttribute('data-to') || '',
                          label: (el.getAttribute('data-label') || (el.querySelector('title') && el.querySelector('title').textContent) || '').trim(),
                          lineEl
                        });
                      });
                    }

                    container.classList.add('is-interactive');

                    const vb = svg.viewBox && svg.viewBox.baseVal;
                    const natW = (vb && vb.width > 0) ? vb.width : (numAttr(svg, 'width') || 800);
                    let zoom = 1.0;
                    const applyZoom = (mode) => {
                      if (mode === 'fit') {
                        svg.style.width = '100%';
                        svg.style.maxWidth = '100%';
                      } else {
                        svg.style.maxWidth = 'none';
                        svg.style.width = Math.round(natW * zoom) + 'px';
                      }
                      svg.style.height = 'auto';
                    };
                    applyZoom('fixed');

                    const toolbar = mkEl('div', 'diagram-toolbar');
                    const searchBox = mkEl('input', 'diagram-search');
                    searchBox.type = 'search';
                    searchBox.placeholder = 'Find component…';
                    searchBox.setAttribute('aria-label', 'Find component in diagram');

                    const modeGroup = mkEl('div', 'diagram-mode-group');
                    modeGroup.setAttribute('role', 'group');
                    modeGroup.setAttribute('aria-label', 'Connection direction filter');
                    const dirButtons = {};
                    const addDirBtn = (val, label) => {
                      const b = mkEl('button', 'diagram-btn' + (val === 'all' ? ' is-active' : ''), label);
                      b.type = 'button';
                      b.addEventListener('click', () => {
                        direction = val;
                        Object.entries(dirButtons).forEach(([k, btn]) => btn.classList.toggle('is-active', k === val));
                        renderState();
                      });
                      dirButtons[val] = b;
                      modeGroup.appendChild(b);
                    };
                    addDirBtn('all', 'All');
                    addDirBtn('in', 'In ←');
                    addDirBtn('out', 'Out →');

                    const chainBtn = mkEl('button', 'diagram-btn', 'Chain');
                    chainBtn.type = 'button';
                    chainBtn.title = 'Toggle full multi-hop upstream/downstream chain';
                    chainBtn.addEventListener('click', () => {
                      chainMode = !chainMode;
                      chainBtn.classList.toggle('is-active', chainMode);
                      renderState();
                    });
                    modeGroup.appendChild(chainBtn);

                    const zoomGroup = mkEl('div', 'diagram-zoom-group');
                    const mkZoomBtn = (label, fn) => {
                      const b = mkEl('button', 'diagram-btn', label);
                      b.type = 'button';
                      b.addEventListener('click', fn);
                      return b;
                    };
                    zoomGroup.append(
                      mkZoomBtn('−', () => { zoom = Math.max(0.4, zoom - 0.15); applyZoom('fixed'); }),
                      mkZoomBtn('100%', () => { zoom = 1.0; applyZoom('fixed'); }),
                      mkZoomBtn('Fit', () => applyZoom('fit')),
                      mkZoomBtn('+', () => { zoom = Math.min(2.5, zoom + 0.15); applyZoom('fixed'); }),
                      mkZoomBtn('Reset', () => clearFocus(true))
                    );

                    toolbar.append(searchBox, modeGroup, zoomGroup);
                    const inspector = mkEl('div', 'diagram-inspector');
                    inspector.setAttribute('aria-live', 'polite');

                    container.parentNode.insertBefore(toolbar, container);
                    container.parentNode.insertBefore(inspector, container);

                    let selectedNodeId = null;
                    let selectedEdgeKey = null;
                    let direction = 'all';
                    let chainMode = false;
                    let query = '';
                    let suppressClick = false;

                    const restoreLine = (edge) => {
                      if (!edge.lineEl) return;
                      const orig = origMarkers.get(edge.lineEl) || '';
                      if (orig) edge.lineEl.setAttribute('marker-end', orig);
                      else edge.lineEl.removeAttribute('marker-end');
                    };

                    const setMarker = (edge, markerId) => {
                      if (!edge.lineEl || edge.el.classList.contains('is-violation')) return;
                      const orig = origMarkers.get(edge.lineEl) || '';
                      if (orig) edge.lineEl.setAttribute('marker-end', 'url(#' + markerId + ')');
                    };

                    const scrollNodeIntoView = (nodeId) => {
                      const els = nodeGroups.get(nodeId);
                      if (!els || !els[0]) return;
                      const rect = els[0].querySelector('rect');
                      if (!rect) return;
                      const box = svg.getBoundingClientRect();
                      const scale = box.width > 0 ? box.width / natW : 1;
                      const cx = (numAttr(rect, 'x') + numAttr(rect, 'width') / 2) * scale;
                      const cy = (numAttr(rect, 'y') + numAttr(rect, 'height') / 2) * scale;
                      container.scrollTo({
                        left: Math.max(0, cx - container.clientWidth / 2),
                        top: Math.max(0, cy - container.clientHeight / 2),
                        behavior: 'smooth'
                      });
                    };

                    const collectBfs = (startId, reverse) => {
                      const foundEdges = new Set();
                      const foundNodes = new Set();
                      const visited = new Set([startId]);
                      const queue = [startId];
                      while (queue.length > 0) {
                        const cur = queue.shift();
                        edges.forEach((e) => {
                          const hit = reverse ? e.dst === cur : e.src === cur;
                          if (!hit) return;
                          foundEdges.add(e);
                          const next = reverse ? e.src : e.dst;
                          if (next !== startId) {
                            foundNodes.add(next);
                            if (chainMode && !visited.has(next)) {
                              visited.add(next);
                              queue.push(next);
                            }
                          }
                        });
                        if (!chainMode) break;
                      }
                      return { foundEdges, foundNodes };
                    };

                    const makeNodeChip = (nodeId, cls) => {
                      const chip = mkEl('button', 'inspector-chip ' + cls, nodeLabels.get(nodeId) || nodeId);
                      chip.type = 'button';
                      chip.addEventListener('click', () => {
                        selectNode(nodeId);
                        scrollNodeIntoView(nodeId);
                      });
                      return chip;
                    };

                    const resetClasses = () => {
                      svg.classList.remove('is-focusing');
                      nodeGroups.forEach((els) => {
                        els.forEach((el) => el.classList.remove(
                          'is-selected', 'is-neighbor-in', 'is-neighbor-out', 'is-neighbor-both', 'is-match', 'is-dim'));
                      });
                      edges.forEach((e) => {
                        e.el.classList.remove('is-in', 'is-out', 'is-active', 'is-dim');
                        if (e.pill) e.pill.classList.remove('is-in', 'is-out', 'is-active', 'is-dim');
                        restoreLine(e);
                      });
                      lifelinesById.forEach((line) => line.classList.remove('is-active', 'is-dim'));
                      seqNotes.forEach((n) => n.classList.remove('is-dim'));
                      subgraphs.forEach((sg) => sg.el.classList.remove('is-dim', 'is-active'));
                    };

                    const renderInspector = (info) => {
                      inspector.textContent = '';
                      if (info.mode === 'idle') {
                        inspector.appendChild(mkEl(
                          'span',
                          'inspector-hint',
                          'Click any component or arrow to focus its incoming/outgoing paths · Drag canvas to pan · Esc to reset'));
                        return;
                      }

                      if (info.mode === 'search') {
                        const header = mkEl('div', 'inspector-header');
                        header.appendChild(mkEl(
                          'span',
                          'inspector-title',
                          'Matches (' + info.matches.length + ') — press Enter to focus first'));
                        inspector.appendChild(header);
                        if (info.matches.length > 0) {
                          const row = mkEl('div', 'inspector-group');
                          info.matches.slice(0, 12).forEach((id) => row.appendChild(makeNodeChip(id, 'chip-match')));
                          inspector.appendChild(row);
                        }
                        return;
                      }

                      if (info.mode === 'edge') {
                        const e = info.edge;
                        const header = mkEl('div', 'inspector-header');
                        const flow = mkEl('div', 'inspector-edge-flow');
                        flow.appendChild(makeNodeChip(e.src, 'chip-in'));
                        flow.appendChild(mkEl('span', 'inspector-arrow', e.label ? ' ──(' + e.label + ')──▶ ' : ' ────▶ '));
                        flow.appendChild(makeNodeChip(e.dst, 'chip-out'));
                        header.appendChild(flow);
                        const clearBtn = mkEl('button', 'inspector-clear', 'Clear ×');
                        clearBtn.type = 'button';
                        clearBtn.addEventListener('click', () => clearFocus(false));
                        header.appendChild(clearBtn);
                        inspector.appendChild(header);
                        return;
                      }

                      const label = nodeLabels.get(selectedNodeId) || selectedNodeId;
                      const header = mkEl('div', 'inspector-header');
                      const left = mkEl('div', 'inspector-header-left');
                      left.appendChild(mkEl('span', 'inspector-title', label));
                      left.appendChild(mkEl(
                        'span',
                        'inspector-stats',
                        'In: ' + info.inNodes.size + ' · Out: ' + info.outNodes.size + ' · ' + (chainMode ? 'Chain' : '1-hop')));
                      header.appendChild(left);

                      const right = mkEl('div', 'inspector-header-right');
                      const cqrsId = byName.get(label) || byName.get(selectedNodeId);
                      if (cqrsId) {
                        const jumpBtn = mkEl('button', 'inspector-clear', 'Inspect CQRS slice ↗');
                        jumpBtn.type = 'button';
                        jumpBtn.addEventListener('click', () => openRequestInspector(cqrsId));
                        right.appendChild(jumpBtn);
                      }
                      const clearBtn = mkEl('button', 'inspector-clear', 'Clear ×');
                      clearBtn.type = 'button';
                      clearBtn.addEventListener('click', () => clearFocus(false));
                      right.appendChild(clearBtn);
                      header.appendChild(right);
                      inspector.appendChild(header);

                      if (info.inNodes.size > 0) {
                        const row = mkEl('div', 'inspector-group');
                        row.appendChild(mkEl('span', 'inspector-group-label chip-in-lbl', '← In'));
                        [...info.inNodes].forEach((id) => row.appendChild(makeNodeChip(id, 'chip-in')));
                        inspector.appendChild(row);
                      }
                      if (info.outNodes.size > 0) {
                        const row = mkEl('div', 'inspector-group');
                        row.appendChild(mkEl('span', 'inspector-group-label chip-out-lbl', 'Out →'));
                        [...info.outNodes].forEach((id) => row.appendChild(makeNodeChip(id, 'chip-out')));
                        inspector.appendChild(row);
                      }
                    };

                    const renderState = () => {
                      resetClasses();

                      if (selectedNodeId && nodeGroups.has(selectedNodeId)) {
                        svg.classList.add('is-focusing');
                        const up = (direction === 'all' || direction === 'in')
                          ? collectBfs(selectedNodeId, true)
                          : { foundEdges: new Set(), foundNodes: new Set() };
                        const down = (direction === 'all' || direction === 'out')
                          ? collectBfs(selectedNodeId, false)
                          : { foundEdges: new Set(), foundNodes: new Set() };

                        const visibleNodes = new Set([selectedNodeId, ...up.foundNodes, ...down.foundNodes]);

                        edges.forEach((e) => {
                          const isIn = up.foundEdges.has(e);
                          const isOut = down.foundEdges.has(e);
                          if (isIn && isOut) {
                            e.el.classList.add('is-active');
                            if (e.pill) e.pill.classList.add('is-active');
                            setMarker(e, 'arrow-active');
                          } else if (isIn) {
                            e.el.classList.add('is-in');
                            if (e.pill) e.pill.classList.add('is-in');
                            setMarker(e, 'arrow-in');
                          } else if (isOut) {
                            e.el.classList.add('is-out');
                            if (e.pill) e.pill.classList.add('is-out');
                            setMarker(e, 'arrow-out');
                          } else {
                            e.el.classList.add('is-dim');
                            if (e.pill) e.pill.classList.add('is-dim');
                          }
                        });

                        nodeGroups.forEach((els, id) => {
                          const isSel = id === selectedNodeId;
                          const isIn = up.foundNodes.has(id);
                          const isOut = down.foundNodes.has(id);
                          els.forEach((el) => {
                            if (isSel) el.classList.add('is-selected');
                            else if (isIn && isOut) el.classList.add('is-neighbor-both');
                            else if (isIn) el.classList.add('is-neighbor-in');
                            else if (isOut) el.classList.add('is-neighbor-out');
                            else el.classList.add('is-dim');
                          });
                        });

                        lifelinesById.forEach((line, pid) => {
                          if (visibleNodes.has(pid)) line.classList.add('is-active');
                          else line.classList.add('is-dim');
                        });
                        seqNotes.forEach((n) => n.classList.add('is-dim'));
                        subgraphs.forEach((sg) => {
                          const hasVisible = [...visibleNodes].some((id) => sg.members.has(id));
                          sg.el.classList.add(hasVisible ? 'is-active' : 'is-dim');
                        });

                        renderInspector({ mode: 'node', inNodes: up.foundNodes, outNodes: down.foundNodes });
                        return;
                      }

                      if (selectedEdgeKey) {
                        const targetEdge = edges.find((e) => e.key === selectedEdgeKey);
                        if (targetEdge) {
                          svg.classList.add('is-focusing');
                          const activeNodes = new Set([targetEdge.src, targetEdge.dst]);
                          edges.forEach((e) => {
                            if (e === targetEdge) {
                              e.el.classList.add('is-active');
                              if (e.pill) e.pill.classList.add('is-active');
                              setMarker(e, 'arrow-active');
                            } else {
                              e.el.classList.add('is-dim');
                              if (e.pill) e.pill.classList.add('is-dim');
                            }
                          });
                          nodeGroups.forEach((els, id) => {
                            els.forEach((el) => {
                              if (id === targetEdge.src && id === targetEdge.dst) el.classList.add('is-selected');
                              else if (id === targetEdge.src) el.classList.add('is-neighbor-in');
                              else if (id === targetEdge.dst) el.classList.add('is-neighbor-out');
                              else el.classList.add('is-dim');
                            });
                          });
                          lifelinesById.forEach((line, pid) => {
                            line.classList.add(activeNodes.has(pid) ? 'is-active' : 'is-dim');
                          });
                          seqNotes.forEach((n) => n.classList.add('is-dim'));
                          subgraphs.forEach((sg) => {
                            const hasVisible = [...activeNodes].some((id) => sg.members.has(id));
                            sg.el.classList.add(hasVisible ? 'is-active' : 'is-dim');
                          });
                          renderInspector({ mode: 'edge', edge: targetEdge });
                          return;
                        }
                      }

                      if (query.length > 0) {
                        svg.classList.add('is-focusing');
                        const matches = [];
                        const matchSet = new Set();
                        nodeLabels.forEach((lbl, id) => {
                          if (lbl.toLowerCase().includes(query) || id.toLowerCase().includes(query)) {
                            matches.push(id);
                            matchSet.add(id);
                          }
                        });
                        nodeGroups.forEach((els, id) => {
                          els.forEach((el) => el.classList.add(matchSet.has(id) ? 'is-match' : 'is-dim'));
                        });
                        edges.forEach((e) => {
                          const hit = matchSet.has(e.src) || matchSet.has(e.dst);
                          e.el.classList.add(hit ? 'is-active' : 'is-dim');
                          if (e.pill) e.pill.classList.add(hit ? 'is-active' : 'is-dim');
                        });
                        lifelinesById.forEach((line, pid) => {
                          line.classList.add(matchSet.has(pid) ? 'is-active' : 'is-dim');
                        });
                        seqNotes.forEach((n) => n.classList.add('is-dim'));
                        subgraphs.forEach((sg) => {
                          const hasVisible = matches.some((id) => sg.members.has(id));
                          sg.el.classList.add(hasVisible ? 'is-active' : 'is-dim');
                        });
                        renderInspector({ mode: 'search', matches });
                        return;
                      }

                      renderInspector({ mode: 'idle' });
                    };

                    const selectNode = (id) => {
                      selectedEdgeKey = null;
                      selectedNodeId = selectedNodeId === id ? null : id;
                      renderState();
                    };

                    const selectEdge = (key) => {
                      selectedNodeId = null;
                      selectedEdgeKey = selectedEdgeKey === key ? null : key;
                      renderState();
                    };

                    const clearFocus = (resetZoomToo) => {
                      selectedNodeId = null;
                      selectedEdgeKey = null;
                      query = '';
                      searchBox.value = '';
                      if (resetZoomToo) {
                        zoom = 1.0;
                        applyZoom('fixed');
                        direction = 'all';
                        chainMode = false;
                        chainBtn.classList.remove('is-active');
                        Object.entries(dirButtons).forEach(([k, btn]) => btn.classList.toggle('is-active', k === 'all'));
                      }
                      renderState();
                    };

                    nodeGroups.forEach((els, id) => {
                      els.forEach((el) => {
                        el.addEventListener('click', (e) => {
                          if (suppressClick) return;
                          e.stopPropagation();
                          selectNode(id);
                        });
                        el.addEventListener('dblclick', (e) => {
                          e.stopPropagation();
                          const lbl = nodeLabels.get(id) || id;
                          const anchor = byName.get(lbl) || byName.get(id);
                          if (anchor) openRequestInspector(anchor);
                        });
                        el.addEventListener('keydown', (e) => {
                          if (e.key === 'Enter' || e.key === ' ') {
                            e.preventDefault();
                            selectNode(id);
                          } else if (e.key === 'Escape') {
                            clearFocus(false);
                          }
                        });
                      });
                    });

                    edges.forEach((edge) => {
                      edge.el.addEventListener('click', (e) => {
                        if (suppressClick) return;
                        e.stopPropagation();
                        selectEdge(edge.key);
                      });
                      if (edge.pill) {
                        edge.pill.addEventListener('click', (e) => {
                          if (suppressClick) return;
                          e.stopPropagation();
                          selectEdge(edge.key);
                        });
                      }
                    });

                    svg.addEventListener('click', (e) => {
                      if (suppressClick) return;
                      if (e.target === svg || e.target.classList.contains('svg-sg-box'))
                        clearFocus(false);
                    });

                    searchBox.addEventListener('input', () => {
                      selectedNodeId = null;
                      selectedEdgeKey = null;
                      query = searchBox.value.trim().toLowerCase();
                      renderState();
                    });

                    searchBox.addEventListener('keydown', (e) => {
                      if (e.key === 'Enter' && query.length > 0) {
                        e.preventDefault();
                        for (const [id, lbl] of nodeLabels.entries()) {
                          if (lbl.toLowerCase().includes(query) || id.toLowerCase().includes(query)) {
                            selectNode(id);
                            scrollNodeIntoView(id);
                            break;
                          }
                        }
                      } else if (e.key === 'Escape') {
                        clearFocus(false);
                      }
                    });

                    let isDown = false;
                    let dragged = false;
                    let startX = 0;
                    let startY = 0;
                    let startScrollX = 0;
                    let startScrollY = 0;

                    container.addEventListener('mousedown', (e) => {
                      if (e.button !== 0) return;
                      isDown = true;
                      dragged = false;
                      startX = e.clientX;
                      startY = e.clientY;
                      startScrollX = container.scrollLeft;
                      startScrollY = container.scrollTop;
                    });
                    window.addEventListener('mousemove', (e) => {
                      if (!isDown) return;
                      const dx = e.clientX - startX;
                      const dy = e.clientY - startY;
                      if (!dragged && Math.hypot(dx, dy) > 4) {
                        dragged = true;
                        container.classList.add('is-dragging');
                      }
                      if (dragged) {
                        container.scrollLeft = startScrollX - dx;
                        container.scrollTop = startScrollY - dy;
                      }
                    });
                    const endPan = () => {
                      if (!isDown) return;
                      isDown = false;
                      container.classList.remove('is-dragging');
                      if (dragged) {
                        suppressClick = true;
                        setTimeout(() => { suppressClick = false; }, 0);
                      }
                    };
                    window.addEventListener('mouseup', endPan);

                    container.addEventListener('wheel', (e) => {
                      if (e.ctrlKey || e.metaKey) {
                        e.preventDefault();
                        zoom = Math.min(2.5, Math.max(0.4, zoom + (e.deltaY < 0 ? 0.12 : -0.12)));
                        applyZoom('fixed');
                      }
                    }, { passive: false });

                    document.addEventListener('keydown', (e) => {
                      if (e.key === 'Escape' && (selectedNodeId || selectedEdgeKey || query.length > 0))
                        clearFocus(false);
                    });

                    renderState();
                  };

                  document.querySelectorAll('.svg-diagram').forEach(enhanceSvgDiagram);
                })();
                """;
}
