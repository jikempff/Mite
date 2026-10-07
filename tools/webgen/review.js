// Mite Bench review tools: find and filter the component cards, move through
// them from the keyboard, and hand the review over as a GitHub issue draft.
//
// build.py appends this file as the page's last script, after the main
// script, so it can use that script's globals (store, updateProgress, toast).
// The first half is pure functions (MiteReview.*) with no DOM access; the
// regression tests in tests/Mite.Tests/BenchReviewTests.cs run them under
// Jint. The second half wires them to the page when there is a document.
//
// Sources (the rules each part follows):
// - Shneiderman 1994, Dynamic queries for visual information seeking (IEEE
//   Software 11(6)): visual presentation of the query and of the results,
//   rapid, incremental and reversible control, immediate and continuous
//   feedback (the cards hide while you type, no Search button).
// - Hearst 2009, Search User Interfaces, ch. 8: facets with query previews
//   (a count next to each value), avoidance of empty result sets, keyword
//   search running inside the facets, one click to remove a constraint.
// - W3C WAI-ARIA Authoring Practices, Developing a Keyboard Interface:
//   shortcuts never replace Tab navigation, avoid browser / AT / OS keys,
//   follow familiar conventions; focus must never be lost.
// - WCAG 2.1 SC 2.1.4 Character Key Shortcuts: single-key shortcuts can be
//   turned off. SC 4.1.3 Status Messages: the result count is a role=status
//   region, announced without taking focus.
// - GitHub Docs, Creating an issue from a URL query: title and body
//   parameters; any parameter the visitor lacks permission for (labels,
//   assignees) turns the page into a 404, and an over-long URL into a 414.
const MiteReview = (() => {
  'use strict';

  const STATUS = ['all', 'failing', 'unreviewed', 'reviewed', 'noted'];
  const STATUS_LABEL = { all: 'All', failing: 'Failing', unreviewed: 'To review', reviewed: 'Reviewed', noted: 'With notes' };
  // A conservative budget for a github.com URL. GitHub does not document the
  // limit (only that it answers 414); 8 000 characters stays inside the
  // common 8 KiB request-line limit of web servers and proxies.
  const URL_BUDGET = 8000;

  // Case-, accent- and typography-insensitive text for matching:
  // "Mesh Colour Map" ~ "colour", "Scho\u0308ne" ~ "schone", "x\u00b2 \u2212 y\u00b2" ~ "x2 - y2".
  function fold(s) {
    return String(s == null ? '' : s)
      .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
      .replace(/[\u2212\u2010-\u2015]/g, '-')
      .replace(/[\u00b2]/g, '2').replace(/[\u00b3]/g, '3').replace(/[\u00b9]/g, '1')
      .replace(/[\u00b7\u2022]/g, ' ')
      .toLowerCase().replace(/\s+/g, ' ').trim();
  }

  // Terms are AND-ed; "quoted text" is one term.
  function parseQuery(q) {
    const terms = [];
    const re = /"([^"]*)"|(\S+)/g;
    let m;
    const s = String(q == null ? '' : q);
    while ((m = re.exec(s)) !== null) {
      const t = fold(m[1] != null ? m[1] : m[2]);
      if (t) terms.push(t);
    }
    return terms;
  }

  function matches(haystack, terms) {
    const h = fold(haystack);
    for (const t of terms) if (h.indexOf(t) < 0) return false;
    return true;
  }

  function statusOf(card, status) {
    switch (status) {
      case 'failing': return card.failing > 0;
      case 'unreviewed': return !card.reviewed;
      case 'reviewed': return !!card.reviewed;
      case 'noted': return !!(card.note && String(card.note).trim());
      default: return true;
    }
  }

  function normState(state) {
    const s = state || {};
    return {
      q: s.q == null ? '' : String(s.q),
      status: STATUS.indexOf(s.status) >= 0 ? s.status : 'all',
      tab: s.tab ? String(s.tab) : 'all',
    };
  }

  // cards: [{ id, tab, text, failing, reviewed, note }]
  // Returns the visible ids in page order and the query previews: for every
  // status value the count with the text and tab constraints applied, for
  // every tab the count with the text and status constraints applied.
  function filter(cards, state) {
    const st = normState(state);
    const terms = parseQuery(st.q);
    const text = cards.map(c => matches(c.text, terms));
    const visible = [];
    const statusCounts = {};
    STATUS.forEach(s => { statusCounts[s] = 0; });
    const tabCounts = { all: 0 };
    cards.forEach((c, i) => {
      if (!(c.tab in tabCounts)) tabCounts[c.tab] = 0;
      if (!text[i]) return;
      const inTab = st.tab === 'all' || c.tab === st.tab;
      const inStatus = statusOf(c, st.status);
      if (inTab) STATUS.forEach(s => { if (statusOf(c, s)) statusCounts[s]++; });
      if (inStatus) { tabCounts.all++; tabCounts[c.tab]++; }
      if (inTab && inStatus) visible.push(c.id);
    });
    return { visible, statusCounts, tabCounts, terms, state: st };
  }

  // One line for the live region, e.g. "3 of 29 components, Failing, 'asym'" (joined by middle dots).
  function statusLine(result, total) {
    const st = result.state;
    const n = result.visible.length;
    const parts = [n === total ? `All ${total} components` : `${n} of ${total} component${total === 1 ? '' : 's'}`];
    if (st.status !== 'all') parts.push(STATUS_LABEL[st.status]);
    if (st.tab !== 'all') parts.push(st.tab);
    if (result.terms.length) parts.push(result.terms.map(t => '\u201c' + t + '\u201d').join(' '));
    return parts.join(' \u00b7 ');
  }

  // Keyboard map. ev: { key, ctrlKey, altKey, metaKey, editable } where
  // editable is true while focus is in a text field. Escape is the only key
  // that acts inside a field (it leaves the search box). Modifier chords are
  // left to the browser and assistive technology.
  const KEYS = { '/': 'search', 'j': 'next', 'k': 'prev', 'x': 'toggle', 'n': 'note', 'f': 'nextFailing', '?': 'help' };
  function keyAction(ev, singleKeysOn) {
    if (!ev || ev.ctrlKey || ev.altKey || ev.metaKey) return null;
    if (ev.key === 'Escape') return ev.editable ? 'leave' : null;
    if (ev.editable || !singleKeysOn) return null;
    return Object.prototype.hasOwnProperty.call(KEYS, ev.key) ? KEYS[ev.key] : null;
  }

  // Index of the next (dir = +1) or previous (-1) card in a list of visible
  // ids, starting from the current id (or from nothing). Stops at the ends.
  function step(ids, current, dir) {
    if (!ids.length) return -1;
    const i = ids.indexOf(current);
    if (i < 0) return dir > 0 ? 0 : ids.length - 1;
    return Math.max(0, Math.min(ids.length - 1, i + (dir > 0 ? 1 : -1)));
  }

  // The filter state lives in the page URL (?q=...&show=...&tab=...) so a filtered
  // view can be bookmarked or sent; defaults are left out.
  function toSearch(state) {
    const st = normState(state);
    const p = [];
    if (st.q.trim()) p.push('q=' + encodeURIComponent(st.q.trim()));
    if (st.status !== 'all') p.push('show=' + st.status);
    if (st.tab !== 'all') p.push('tab=' + encodeURIComponent(st.tab));
    return p.length ? '?' + p.join('&') : '';
  }
  function fromSearch(search, tabs) {
    const st = { q: '', status: 'all', tab: 'all' };
    String(search || '').replace(/^\?/, '').split('&').forEach(kv => {
      if (!kv) return;
      const j = kv.indexOf('=');
      const k = j < 0 ? kv : kv.slice(0, j);
      let v = j < 0 ? '' : kv.slice(j + 1).replace(/\+/g, ' ');
      try { v = decodeURIComponent(v); } catch (e) { return; }
      if (k === 'q') st.q = v;
      else if (k === 'show' && STATUS.indexOf(v) >= 0) st.status = v;
      else if (k === 'tab' && (!tabs || tabs.indexOf(v) >= 0)) st.tab = v;
    });
    return st;
  }

  function oneLine(s) { return String(s == null ? '' : s).replace(/\s+/g, ' ').trim(); }

  // review: { generated, cards: [{ name, tab, reviewed, note, failing: [{ label, detail }] }] }
  // GitHub-flavoured Markdown with a task list, failing self-tests first.
  function issueMarkdown(review) {
    const cards = review.cards || [];
    const total = cards.length;
    const ok = cards.filter(c => c.reviewed).length;
    const fails = [];
    cards.forEach(c => (c.failing || []).forEach(f => fails.push({ name: c.name, label: f.label, detail: f.detail })));
    const lines = [];
    lines.push(`**${ok} of ${total}** components marked as making sense \u00b7 **${fails.length}** self-test${fails.length === 1 ? '' : 's'} failing` +
      (review.generated ? ` \u00b7 ${oneLine(review.generated)}` : ''));
    lines.push('');
    if (fails.length) {
      lines.push('### Failing self-tests', '');
      fails.forEach(f => lines.push(`- **${f.name}**: ${oneLine(f.label)}${f.detail ? ' \u2014 `' + oneLine(f.detail).replace(/`/g, "'") + '`' : ''}`));
      lines.push('');
    }
    const shown = cards.filter(c => c.reviewed || oneLine(c.note));
    if (shown.length) {
      lines.push('### Components', '');
      shown.forEach(c => lines.push(`- [${c.reviewed ? 'x' : ' '}] **${c.name}**${oneLine(c.note) ? ' \u2014 ' + oneLine(c.note) : ''}`));
      lines.push('');
    }
    const rest = cards.filter(c => !c.reviewed && !oneLine(c.note));
    if (rest.length) lines.push(`Not reviewed yet (${rest.length}): ${rest.map(c => c.name).join(', ')}.`);
    return lines.join('\n').replace(/\n+$/, '') + '\n';
  }

  function issueTitle(review) {
    const cards = review.cards || [];
    const ok = cards.filter(c => c.reviewed).length;
    const nf = cards.reduce((a, c) => a + (c.failing || []).length, 0);
    return `Mite Bench review: ${ok}/${cards.length} make sense` + (nf ? `, ${nf} self-test${nf === 1 ? '' : 's'} failing` : '');
  }

  // A new-issue link. Only title and body: labels or assignees would 404 for
  // anyone without triage rights on the repository. When the body does not
  // fit the budget, whole lines are kept from the top (failing tests first)
  // and a last line asks to paste the full text from the clipboard.
  function issueUrl(repo, title, body, budget) {
    const max = budget || URL_BUDGET;
    const base = `https://github.com/${repo}/issues/new?title=${encodeURIComponent(title)}&body=`;
    const full = base + encodeURIComponent(body);
    if (full.length <= max) return { url: full, complete: true, keptLines: body.split('\n').length };
    const tail = '\n\n_The review was too long for a link (' + body.length + ' characters). The full text is on your clipboard: select this line and paste it over._';
    const lines = body.split('\n');
    let kept = 0;
    let used = base.length + encodeURIComponent(tail).length;
    for (const l of lines) {
      const add = encodeURIComponent((kept ? '\n' : '') + l).length;
      if (used + add > max) break;
      used += add; kept++;
    }
    const cut = lines.slice(0, kept).join('\n') + tail;
    return { url: base + encodeURIComponent(cut), complete: false, keptLines: kept };
  }

  return { STATUS, STATUS_LABEL, URL_BUDGET, KEYS, fold, parseQuery, matches, filter, statusLine, keyAction, step, toSearch, fromSearch, issueMarkdown, issueTitle, issueUrl };
})();
if (typeof module !== 'undefined' && module.exports) module.exports = MiteReview;

// ---------------------------------------------------------------- page wiring
if (typeof document !== 'undefined' && document.querySelector('.card')) (() => {
  const R = MiteReview;
  const REPO = 'jikempff/Mite';
  const st = { q: '', status: 'all', tab: 'all' };
  const mem = {
    get(k) { try { return localStorage.getItem('mite-bench:' + k); } catch (e) { return null; } },
    set(k, v) { try { localStorage.setItem('mite-bench:' + k, v); } catch (e) { } },
  };
  let singleKeys = mem.get('keys') !== 'off';
  const say = msg => { if (typeof toast === 'function') toast(msg); };

  // ---- cards as data
  const cardEls = [...document.querySelectorAll('article.card')];
  const tabOf = el => { const s = el.closest('section.tab'); const h = s && s.querySelector('.tab-h h2'); return h ? h.textContent.trim() : ''; };
  const tabs = [];
  cardEls.forEach(el => { const t = tabOf(el); if (tabs.indexOf(t) < 0) tabs.push(t); });
  const textOf = el => [el.dataset.name, tabOf(el), ...[...el.querySelectorAll('.nickname, .desc, .expect li, .checks b, td.pn, td.pd')].map(x => x.textContent)].join(' \n ');
  const texts = new Map(cardEls.map(el => [el.id, textOf(el)]));
  const failingOf = el => [...el.querySelectorAll('.checks li')].filter(li => li.querySelector('.chip.fail'))
    .map(li => ({ label: li.querySelector('b').textContent, detail: li.querySelector('.ct').textContent }));
  const model = () => cardEls.map(el => ({
    id: el.id, name: el.dataset.name, tab: tabOf(el), text: texts.get(el.id),
    failing: failingOf(el).length, failingList: failingOf(el),
    reviewed: !!(el.querySelector('[data-ok]') || {}).checked,
    note: (el.querySelector('[data-note]') || {}).value || '',
  }));

  // ---- markup: the find box and facets at the top of the component list
  const nav = document.querySelector('nav.nav');
  const box = document.createElement('div');
  box.className = 'finder';
  box.setAttribute('role', 'search');
  box.innerHTML =
    '<label class="finder-l" for="flt-q">Find a component</label>' +
    '<div class="finder-q"><input id="flt-q" type="search" autocomplete="off" spellcheck="false" placeholder="name, input, shape\u2026" aria-describedby="flt-status" aria-keyshortcuts="/">' +
    '<button type="button" class="finder-x" id="flt-clear" hidden aria-label="Clear the filters">Clear</button></div>' +
    '<div class="finder-s" role="group" aria-label="Show">' + R.STATUS.map(s => `<button type="button" data-status="${s}" aria-pressed="${s === 'all'}">${R.STATUS_LABEL[s]} <span class="n"></span></button>`).join('') + '</div>' +
    '<label class="finder-l" for="flt-tab">Tab</label><select id="flt-tab"><option value="all">All tabs</option>' + tabs.map(t => `<option value="${t}">${t}</option>`).join('') + '</select>' +
    '<p class="finder-st" id="flt-status" role="status"></p>';
  nav.insertBefore(box, nav.firstChild);
  const q = box.querySelector('#flt-q'), sel = box.querySelector('#flt-tab'), stEl = box.querySelector('#flt-status'), clearBtn = box.querySelector('#flt-clear');

  const empty = document.createElement('div');
  empty.className = 'finder-empty'; empty.hidden = true;
  empty.innerHTML = '<p>No component matches these filters.</p><button type="button">Clear the filters</button>';
  const main = document.querySelector('main');
  main.insertBefore(empty, main.querySelector('section.tab'));

  const intro = document.querySelector('.intro');
  if (intro) {
    const d = document.createElement('div');
    d.innerHTML = '<h4>Find and report</h4><p>Type in \u201cFind a component\u201d or pick Failing, To review or a tab; the view is kept in the page address. Press <kbd>?</kbd> for the keyboard shortcuts. \u201cOpen as GitHub issue\u201d turns your ticks and notes into a new issue on the Mite repository.</p>';
    intro.appendChild(d);
  }

  const tools = nav.querySelector('.tools');
  const mk = (id, text) => { const b = document.createElement('button'); b.type = 'button'; b.id = id; b.textContent = text; tools.appendChild(b); return b; };
  const issueBtn = mk('issue', 'Open as GitHub issue');
  const mdBtn = mk('issue-md', 'Copy as Markdown');
  const keysBtn = mk('keys', 'Keyboard shortcuts');
  keysBtn.setAttribute('aria-keyshortcuts', '?');

  const dlg = document.createElement('dialog');
  dlg.className = 'keys-dlg'; dlg.setAttribute('aria-labelledby', 'keys-h');
  const rows = [['/', 'Find a component'], ['Esc', 'Leave the find box'], ['j / k', 'Next / previous component'], ['x', 'Tick or untick \u201cMakes sense\u201d'],
    ['n', 'Write a note on this component'], ['f', 'Next component with a failing self-test'], ['?', 'This list']];
  dlg.innerHTML = '<h2 id="keys-h">Keyboard shortcuts</h2><table>' + rows.map(([k, t]) => `<tr><td><kbd>${k}</kbd></td><td>${t}</td></tr>`).join('') + '</table>' +
    '<label class="keys-on"><input type="checkbox" id="keys-on"> Single-key shortcuts (all but Esc)</label>' +
    '<p class="keys-note">They never act while you type in a field. Turn them off if they get in the way of your screen reader or speech input; Tab and the find box work either way.</p>' +
    '<form method="dialog"><button>Close</button></form>';
  document.body.appendChild(dlg);
  const keysOn = dlg.querySelector('#keys-on');
  keysOn.checked = singleKeys;
  keysOn.addEventListener('change', () => { singleKeys = keysOn.checked; mem.set('keys', singleKeys ? 'on' : 'off'); });
  const openHelp = () => { if (dlg.showModal) dlg.showModal(); else dlg.setAttribute('open', ''); };
  keysBtn.addEventListener('click', openHelp);

  // ---- apply the filter
  const navA = id => document.querySelector(`.nav a[data-nav="${id}"]`);
  let visibleIds = cardEls.map(el => el.id);
  function apply(push) {
    const res = R.filter(model(), st);
    visibleIds = res.visible;
    const vis = new Set(res.visible);
    cardEls.forEach(el => { el.hidden = !vis.has(el.id); const a = navA(el.id); if (a) a.hidden = el.hidden; });
    document.querySelectorAll('main section.tab').forEach(s => { s.hidden = !s.querySelector('article.card:not([hidden])'); });
    document.querySelectorAll('.nav .navtab').forEach(g => { g.hidden = !g.querySelector('a[data-nav]:not([hidden])'); });
    box.querySelectorAll('[data-status]').forEach(b => {
      const s = b.dataset.status, n = res.statusCounts[s];
      b.querySelector('.n').textContent = n;
      b.setAttribute('aria-pressed', String(s === st.status));
      b.disabled = n === 0 && s !== st.status; // no dead ends, but the active one can always be left
    });
    [...sel.options].forEach(o => { const n = res.tabCounts[o.value] || 0; o.textContent = (o.value === 'all' ? 'All tabs' : o.value) + ' (' + n + ')'; o.disabled = n === 0 && o.value !== st.tab; });
    sel.value = st.tab;
    stEl.textContent = R.statusLine(res, cardEls.length);
    empty.hidden = res.visible.length > 0;
    const active = !!(st.q.trim() || st.status !== 'all' || st.tab !== 'all');
    clearBtn.hidden = !active;
    if (push !== false) { try { history.replaceState(null, '', location.pathname + R.toSearch(st) + location.hash); } catch (e) { } }
  }
  function clearAll() { st.q = ''; st.status = 'all'; st.tab = 'all'; q.value = ''; apply(); q.focus(); }
  q.addEventListener('input', () => { st.q = q.value; apply(); });
  sel.addEventListener('change', () => { st.tab = sel.value; apply(); });
  box.querySelectorAll('[data-status]').forEach(b => b.addEventListener('click', () => { st.status = b.dataset.status; apply(); }));
  clearBtn.addEventListener('click', clearAll);
  empty.querySelector('button').addEventListener('click', clearAll);
  // ticks, notes and test results change the facet counts
  document.addEventListener('change', e => { if (e.target.matches('[data-ok]')) apply(false); });
  document.addEventListener('input', e => { if (e.target.matches('[data-note]')) apply(false); });

  // ---- current card and keyboard
  let current = null;
  const navObs = new IntersectionObserver(es => es.forEach(en => { if (en.isIntersecting && !en.target.hidden) current = en.target.id; }), { rootMargin: '-10% 0px -80% 0px' });
  cardEls.forEach(el => navObs.observe(el));
  cardEls.forEach(el => { const h = el.querySelector('h3'); if (h) h.tabIndex = -1; el.addEventListener('focusin', () => { current = el.id; }); });
  function go(id) {
    const el = document.getElementById(id); if (!el) return;
    current = id;
    const h = el.querySelector('h3');
    el.scrollIntoView({ block: 'start', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    if (h) h.focus({ preventScroll: true });
  }
  const editable = t => !!t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName) && !(t.tagName === 'INPUT' && /^(checkbox|radio|button|range)$/.test(t.type)));
  document.addEventListener('keydown', e => {
    if (e.defaultPrevented || e.isComposing) return;
    if (dlg.open && e.key !== 'Escape') return;
    const act = R.keyAction({ key: e.key, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey, editable: editable(e.target) }, singleKeys);
    if (!act) return;
    if (act === 'leave') { if (e.target === q && q.value) { q.value = ''; st.q = ''; apply(); } else e.target.blur(); e.preventDefault(); return; }
    e.preventDefault();
    if (act === 'search') { q.focus(); q.select(); return; }
    if (act === 'help') { openHelp(); return; }
    const cur = visibleIds.indexOf(current) >= 0 ? current : null;
    if (act === 'next' || act === 'prev') { const i = R.step(visibleIds, cur, act === 'next' ? 1 : -1); if (i >= 0) go(visibleIds[i]); return; }
    if (act === 'nextFailing') {
      const fail = model().filter(c => c.failing > 0 && visibleIds.indexOf(c.id) >= 0).map(c => c.id);
      if (!fail.length) { say('No visible component has a failing self-test'); return; }
      const after = fail.filter(id => visibleIds.indexOf(id) > visibleIds.indexOf(cur));
      go(after.length ? after[0] : fail[0]); return;
    }
    if (!cur) { say('Move to a component first (j)'); return; }
    const el = document.getElementById(cur);
    if (act === 'toggle') { const cb = el.querySelector('[data-ok]'); if (cb) { cb.click(); say((cb.checked ? 'Ticked ' : 'Unticked ') + el.dataset.name); } return; }
    if (act === 'note') { const ta = el.querySelector('[data-note]'); if (ta) { ta.focus(); ta.scrollIntoView({ block: 'nearest' }); } }
  });

  // ---- GitHub issue draft
  function review() {
    const ft = document.querySelector('.foot');
    return { generated: ft ? ft.textContent.split('\u00b7')[0].trim() : '', cards: model().map(c => ({ name: c.name, tab: c.tab, reviewed: c.reviewed, note: c.note, failing: c.failingList })) };
  }
  async function copy(text) {
    try { await navigator.clipboard.writeText(text); return true; } catch (e) { }
    const pre = document.getElementById('summary');
    if (pre) { pre.textContent = text; pre.hidden = false; const r = document.createRange(); r.selectNodeContents(pre); const s = getSelection(); s.removeAllRanges(); s.addRange(r); pre.scrollIntoView({ block: 'nearest' }); }
    return false;
  }
  mdBtn.addEventListener('click', async () => { const md = R.issueMarkdown(review()); say(await copy(md) ? 'Markdown copied' : 'Select and copy the Markdown below'); });
  issueBtn.addEventListener('click', async () => {
    const rv = review(), md = R.issueMarkdown(rv), link = R.issueUrl(REPO, R.issueTitle(rv), md);
    issueBtn.dataset.url = link.url; issueBtn.dataset.complete = String(link.complete);
    if (!link.complete) await copy(md);
    window.open(link.url, '_blank', 'noopener');
    say(link.complete ? 'New issue opened on GitHub' : 'Too long for a link: the full review is on your clipboard');
  });

  // ---- start from the URL (?q=...&show=...&tab=...)
  let initial = { q: '', status: 'all', tab: 'all' };
  try { initial = R.fromSearch(location.search, tabs); } catch (e) { }
  Object.assign(st, initial); q.value = st.q;
  apply(false);
})();
