"""Install the input barrier in a dev checkout, idempotently, never in an installed port."""
from pathlib import Path
import shutil, sys
root = Path(sys.argv[1]).resolve()
assets = Path(__file__).resolve().parents[1] / 'native' / 'visy'
for name in ('checkpoint_io.h', 'checkpoint_input.h', 'runtime_checkpoint_io.c'):
    shutil.copyfile(assets / name, root / 'src' / name)
path = root / 'src' / 'runtime_pad.c'
text = path.read_text(encoding='utf-8')
if 'static void checkpoint_filter_input' not in text:
    anchor = '#include "runtime.h"'
    if text.count(anchor) != 1: raise RuntimeError('runtime_pad include anchor mismatch')
    text = text.replace(anchor, anchor + '\n#include "checkpoint_io.h"\n#include "checkpoint_input.h"')
    anchor = 'static ABI int32_t pad_init(void)'
    if text.count(anchor) != 1: raise RuntimeError('pad_init anchor mismatch')
    helper = '''static void checkpoint_filter_input(PadData *d) {
    static int held;
    int pressed=d->buttons || d->l2>30 || d->r2>30 || d->touch_count;
    if (checkpoint_input_neutral(checkpoint_input_blocked(),pressed,&held)) {
        d->buttons=0; d->l2=d->r2=0;
        d->left_x=d->left_y=d->right_x=d->right_y=128;
        d->touch_count=0; d->touch_held_time=0;
        touch_ids(d); last_sample_buttons=0;
    }
}

'''
    text = text.replace(anchor, helper + anchor)
    anchor = 'sample(data); ++reads;'
    if text.count(anchor) != 1: raise RuntimeError('pad_read_state anchor mismatch')
    text = text.replace(anchor, 'sample(data); checkpoint_filter_input(data); ++reads;')
    path.write_text(text, encoding='utf-8', newline='\n')
