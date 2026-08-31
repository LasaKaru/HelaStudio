// A deliberately tiny client-side router. The point of this fixture is that the
// URL changes without a document load, which is what the shell's title tracking,
// tab-selection patterns, and back-gesture handling all have to cope with.
const views = {
  '/': {
    title: 'Home',
    html: '<p class="marker">SPA HOME</p><p>Client-side routed. No document load happened.</p>',
  },
  '/orders': { title: 'Orders', html: '<p class="marker">SPA ORDERS</p><p>Second route.</p>' },
  '/upload': {
    title: 'Upload',
    html:
      '<p class="marker">SPA UPLOAD</p>' +
      '<form><label>Pick a file <input id="file" type="file" accept="image/*"></label></form>' +
      '<p id="picked">No file chosen.</p>',
  },
  '/long': {
    title: 'Long',
    html: '<p class="marker">SPA LONG</p><div class="tall"></div><p id="bottom">Bottom.</p>',
  },
  '/boom': {
    title: 'Boom',
    html: '<p class="marker">SPA BOOM</p><p>This route throws on render.</p>',
  },
};

function render(path) {
  const view = views[path] ?? views['/'];
  document.title = view.title;
  document.getElementById('view').innerHTML = view.html;

  if (path === '/upload') {
    document.getElementById('file').addEventListener('change', (event) => {
      const file = event.target.files[0];
      document.getElementById('picked').textContent = file
        ? `Chose ${file.name}`
        : 'No file chosen.';
    });
  }

  // A catchable, reproducible error for crash-reporting and console tests.
  if (path === '/boom') {
    throw new Error('Deliberate fixture error from the /boom route');
  }
}

function navigate(path) {
  history.pushState({}, '', path);
  render(path);
}

document.addEventListener('click', (event) => {
  const route = event.target.closest('[data-route]');
  if (!route) return;
  event.preventDefault();
  navigate(route.dataset.route);
});

addEventListener('popstate', () => {
  render(location.pathname);
});
render(location.pathname);
