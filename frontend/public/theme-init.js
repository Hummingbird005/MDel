// Aplică tema salvată înainte de randare (fără flash). Fișier separat, compatibil cu Content-Security-Policy.
try {
  var t = localStorage.getItem('mdel.theme')
  if (t === 'light' || t === 'dark') document.documentElement.setAttribute('data-theme', t)
} catch (e) {}
