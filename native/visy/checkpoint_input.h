/* SPDX-License-Identifier: GPL-3.0-or-later */
#pragma once
/* A held Continue must be released after the barrier, not replayed on release. */
static inline int checkpoint_input_neutral(int blocked, int pressed, int *held) {
    if (blocked) { *held=1; return 1; }
    if (*held && pressed) return 1;
    *held=0;
    return 0;
}
