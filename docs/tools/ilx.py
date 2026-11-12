#!/usr/bin/env python3
"""ilx.py <il file> <Class> [method-regex]  -- print class member list or matching method bodies from monodis output."""
import re, sys
il, cls = sys.argv[1], sys.argv[2]
pat = re.compile(sys.argv[3]) if len(sys.argv) > 3 else None
lines = open(il, errors='replace').read().split('\n')
# find top-level (2-space indent) class
start = None
for i, l in enumerate(lines):
    m = re.match(r'^(\s*)\.class .*\b' + re.escape(cls) + r'\s*$', l)
    if m:
        start, ind = i, m.group(1); break
if start is None: sys.exit('class not found')
end = None
for j in range(start + 1, len(lines)):
    if lines[j].startswith(ind + '} // end of class'):
        end = j; break
body = lines[start:end + 1]
if pat is None:
    for k, l in enumerate(body):
        s = l.strip()
        if s.startswith('.field') or s.startswith('.property') or s.startswith('.class'):
            print(s)
        if s.startswith('.method'):
            print('M: ' + ' '.join(x.strip() for x in body[k+1:k+2]))
    sys.exit()
k = 0
while k < len(body):
    if body[k].strip().startswith('.method'):
        sig = body[k + 1]
        m = re.search(r"'?([\w.<>`]+)'?\s*\(", sig)
        mind = re.match(r'^\s*', body[k]).group(0)
        e = k
        while not body[e].startswith(mind + '} // end of method'):
            e += 1
        if m and pat.search(m.group(1)):
            out = []
            for l in body[k:e + 1]:
                s = l.strip()
                if not s or s.startswith('// Method begins') or s.startswith('.maxstack') or s.startswith('// Code size'): continue
                s = re.sub(r'^IL_([0-9a-f]{4}):\s+', r'\1 ', s)
                out.append(s)
            # strip obfuscation junk
            res = []
            i = 0
            op = lambda x: x.split(' ', 1)[1] if re.match(r'^[0-9a-f]{4} ', x) else x
            while i < len(out):
                o = op(out[i])
                if o == 'dup' and i + 1 < len(out) and op(out[i+1]) == 'pop': i += 2; continue
                if re.match(r'^ldc\.i4', o) and i + 2 < len(out) and op(out[i+1]).startswith('switch (') and re.match(r'^IL_[0-9a-f]{4}\)$', out[i+2]): i += 3; continue
                if o == 'ldc.i4.1' and i + 3 < len(out) and op(out[i+1]).startswith('brtrue') and op(out[i+2]).startswith('ldtoken') and op(out[i+3]) == 'pop': i += 4; continue
                if o == 'ldc.i4.0' and i + 3 < len(out) and op(out[i+1]).startswith('brfalse') and op(out[i+2]).startswith('ldtoken') and op(out[i+3]) == 'pop': i += 4; continue
                s2 = out[i]
                for a in ('class ', 'valuetype ', 'instance ', '[UnityEngine.CoreModule]UnityEngine.', '[mscorlib]System.Collections.Generic.', '[mscorlib]System.'):
                    s2 = s2.replace(a, '')
                res.append(s2)
                i += 1
            print('\n'.join(res)); print()
        k = e
    k += 1
