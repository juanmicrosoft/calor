export const THEME_KEY = 'calor-theme';

// Runs in the document head before content paints, including when storage is blocked.
export const themeScript = `(function(){
  var preference;
  try { preference = localStorage.getItem('${THEME_KEY}'); } catch {}
  var dark = preference === 'dark' || (preference !== 'light' && matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.classList.toggle('dark', dark);
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light';
})();`;
