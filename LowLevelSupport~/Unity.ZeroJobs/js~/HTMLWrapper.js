mergeInto(LibraryManager.library, {
  // Create a variable 'ut' in global scope so that Closure sees it.
  js_html_init__postset : 'var ut;',
  js_html_init__proxy : 'async',
  js_html_init : function() {
    ut = ut || {};
    ut._HTML = ut._HTML || {};

    var html = ut._HTML;
    html.visible = true;
    html.focused = true;
  }
});
