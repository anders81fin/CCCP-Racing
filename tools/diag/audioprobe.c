// Diagnostic helper (2026-11-22): attaches to one thread for a moment and prints its registers and the memory around r13 (the resampler's "this" in the spinning loop of the 4.7.2 release template).
// Usage: audioprobe TID /proc/PID/mem. No libc (a few KB, embedded in StallWatch as base64): raw syscalls.
// Output: "regs rip=.. rsp=.. rbx=.. rbp=.. r12=.. r13=.. ..." and "mem NAME+OFF: word ..." lines (words as 16 hex digits).
typedef unsigned long u64; typedef long i64;
static i64 sc(i64 n, i64 a, i64 b, i64 c, i64 d) { i64 r; register i64 r10 __asm__("r10") = d; __asm__ volatile("syscall" : "=a"(r) : "a"(n), "D"(a), "S"(b), "d"(c), "r"(r10) : "rcx", "r11", "memory"); return r; }
struct regs { u64 r15, r14, r13, r12, rbp, rbx, r11, r10, r9, r8, rax, rcx, rdx, rsi, rdi, orig_rax, rip, cs, eflags, rsp, ss, fs_base, gs_base, ds, es, fs, gs; };
static char buf[8192]; static int len;
static void put(const char *s) { while (*s) buf[len++] = *s++; }
static void hex(u64 v, int digits) { for (int i = digits - 1; i >= 0; i--) buf[len++] = "0123456789abcdef"[(v >> (4 * i)) & 15]; }
static void reg(const char *n, u64 v) { put(" "); put(n); put("="); hex(v, 1 + (v > 15 ? 0 : 0)); }
static int atoi_(const char *s) { int v = 0; while (*s >= '0' && *s <= '9') v = v * 10 + *s++ - '0'; return v; }
static int memfd;
static void dump(const char *name, u64 base, int from, int to) {
    put("mem "); put(name); put("+0x"); hex(from, 3); put(":");
    for (int o = from; o < to; o += 8) { u64 w = 0; put(" "); if (sc(17, memfd, (i64)&w, 8, base + o) != 8) put("????????????????"); else hex(w, 16); }
    put("\n");
}
void cmain(long *sp) {
    int argc = (int)sp[0]; char **argv = (char **)(sp + 1);
    if (argc < 3) sc(60, 2, 0, 0, 0);
    int tid = atoi_(argv[1]); int st = 0; struct regs r;
    if (sc(101, 0x4206, tid, 0, 0) != 0) { put("seize failed\n"); goto out; }
    if (sc(101, 0x4207, tid, 0, 0) != 0) { put("interrupt failed\n"); goto out; }
    // wait4(tid, &st, __WALL=0x40000000, 0)
    if (sc(61, tid, (i64)&st, 0x40000000, 0) != tid) { put("wait failed\n"); goto out; }
    if (sc(101, 12, tid, 0, (i64)&r) != 0) { put("getregs failed\n"); sc(101, 17, tid, 0, 0); goto out; }
    memfd = (int)sc(2, (i64)argv[2], 0, 0, 0);
    put("regs"); const char *nm[] = { "rip", "rsp", "rbx", "rbp", "r12", "r13", "r14", "r15", "rax", "rcx", "rdx", "rsi", "rdi", "r8", "r9" };
    u64 v[] = { r.rip, r.rsp, r.rbx, r.rbp, r.r12, r.r13, r.r14, r.r15, r.rax, r.rcx, r.rdx, r.rsi, r.rdi, r.r8, r.r9 };
    for (int i = 0; i < 15; i++) { put(" "); put(nm[i]); put("=0x"); hex(v[i], 16); }
    put("\n");
    dump("r13", r.r13, 0x560, 0x5c0); dump("rbx", r.rbx, 0, 0x40); dump("rbp", r.rbp, 0, 0x40); dump("r12", r.r12, 0, 0x40);
    sc(101, 17, tid, 0, 0);
out:
    sc(1, 1, (i64)buf, len, 0); sc(60, 0, 0, 0, 0);
}
__asm__(".global _start\n_start:\n mov %rsp,%rdi\n and $-16,%rsp\n call cmain\n");
