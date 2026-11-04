#define _GNU_SOURCE
#include <link.h>
#include <stdint.h>
#include <stdlib.h>
#include <fcntl.h>
#include <unistd.h>

/* Renderer-only GNU loader audit hook. Godot resolves X11 with dlsym, so an
 * ordinary preload hook is insufficient. GPU and movie/audio work continue;
 * only mapping windows to the desktop is suppressed. */
static int same(const char *a, const char *b) {
    while (*a && *a == *b) { ++a; ++b; }
    return *a == *b;
}
static int no_map(void *display, unsigned long window) { return 0; }
unsigned int la_version(unsigned int version) { return LAV_CURRENT; }
unsigned int la_objopen(struct link_map *map, Lmid_t ns, uintptr_t *cookie) {
    return LA_FLG_BINDTO | LA_FLG_BINDFROM;
}
uintptr_t la_symbind64(Elf64_Sym *symbol, unsigned int index, uintptr_t *reference,
                     uintptr_t *definition, unsigned int *flags, const char *name) {
    if (same(name, "XMapWindow") || same(name, "XMapRaised") || same(name, "XMapSubwindows"))
        return (uintptr_t)no_map;
    return symbol->st_value;
}
void la_preinit(uintptr_t *cookie) {
    const char *path = getenv("CCCP_EXPORT_WINDOW_READY");
    if (!path || !*path) return;
    int fd = open(path, O_WRONLY | O_CREAT | O_EXCL | O_CLOEXEC, 0600);
    if (fd >= 0) {
        static const char ready[] = "hidden-x11-v1\n";
        (void)write(fd, ready, sizeof(ready) - 1);
        close(fd);
    }
}
