#!/bin/sh
set -eu
cd "$(dirname "$0")"
case "$(uname -m)" in x86_64) ;; *) echo 'Build this helper on Linux x86_64' >&2; exit 1;; esac
cc -shared -fPIC -O2 -march=x86-64 -mtune=generic -fno-stack-protector -fno-ident -nostartfiles \
    -Wl,-z,defs -Wl,--build-id=none hide_x11.c -o linux-x86_64.so
strip --strip-unneeded linux-x86_64.so
