#ifdef UNITY_WEBGL
#include <Unity/Runtime.h>

#include <emscripten.h>
#include <emscripten/html5.h>
#include <stdio.h>

extern "C" {
    void js_html_init();
}

DOTS_EXPORT(void)
init_html()
{
    js_html_init();
}
#endif