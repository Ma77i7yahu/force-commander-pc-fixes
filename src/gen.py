# Generates PatchData.cs and ..\dist\PATCHES.txt from the tested Force Commander files.
# Usage: gen.py ["path to Force Commander\Resource"]
#
# Inputs (all in the game's Resource folder):
#   focom_original.exe                  untouched 1.1 exe
#   focom.exe                           tested build, every exe option on (path10 + saves + menus)
#   focom_pre_avoidfix.exe              stock + widescreen only (used to prove the group split)
#   forcecommand\fcv1p1_original.rpk    untouched 1.1 patch archive
#   forcecommand\fcv1p1.rpk             tested build (mines + save slots + AI v14)
#   forcecommand\fcv1p1_pre_saveslots.rpk   stock + mines only
#   forcecommand\fcv1p1_pre_ai.rpk      stock + mines + save slots
#   forcecommand\forcecommand_pre_bldgs.rpk + forcecommand.rpk   base archive, before / after
#
# No game data is shipped: exe and patch-archive runs hold only our new bytes, and the one object
# that grew (the AI unit class) is stored as copy-from-your-own-file ops plus the new bytes.
import hashlib, os, struct, sys

R = sys.argv[1] if len(sys.argv) > 1 else r'G:\Game Files\LucasArts\Star Wars Force Commander\Resource'
HERE = os.path.dirname(os.path.abspath(__file__))
F = os.path.join(R, 'forcecommand')

def rd(*p):
    with open(os.path.join(*p), 'rb') as f:
        return f.read()
def md5(b): return hashlib.md5(b).hexdigest()

def runs(a, b, base=0, gap=0):
    """differing runs of equal-length a, b, merging across gaps of <= gap equal bytes"""
    out, i, n = [], 0, min(len(a), len(b))
    while i < n:
        if a[i] != b[i]:
            s = i
            while i < n and a[i] != b[i]:
                i += 1
            if out and s - (out[-1][0] + out[-1][1]) <= gap:
                out[-1] = (out[-1][0], i - out[-1][0])
            else:
                out.append((s, i - s))
        else:
            i += 1
    return [(s + base, l) for s, l in out]

# ---------------------------------------------------------------- exe
stock = rd(R, 'focom_original.exe')
final = rd(R, 'focom.exe')
EXE_STOCK_MD5 = md5(stock)
EXE_FINAL_MD5 = md5(final)
assert len(stock) == 0x4B7000 and len(final) == 0x4FA000, 'unexpected exe sizes'

SEC = {'movement': (0x4B7000, 0x41000, 0x95F000), 'saves': (0x4F8000, 0x1000, 0x9A0000), 'menus': (0x4F9000, 0x1000, 0x9A1000)}
# aspect-dependent floats: written by the installer, so they are not in any group
ASPECT_3D = 0x3C2E04      # 0.75 = (4/3) / aspect
ASPECT_2D = 0x4F9054      # 1.50 = 2 x the above
assert struct.unpack_from('<f', final, ASPECT_3D)[0] == 0.75
assert struct.unpack_from('<f', final, ASPECT_2D)[0] == 1.5

# which option owns each changed range in the stock part of the exe: (first, last, group)
OWNERS = [
    (0x0F6, 0x0F6, 'common'), (0x141, 0x142, 'common'), (0x288, 0x2FF, 'common'),   # section count, SizeOfImage, section table
    (0x1F0, 0x1F1, 'widescreen'), (0x3715B1, 0x3715B8, 'widescreen'), (0x3846A1, 0x3846C8, 'widescreen'), (0x3C2D70, 0x3C2FFF, 'widescreen'),
    (0x237, 0x237, 'saves'), (0x27A2CA, 0x27A2D0, 'saves'), (0x28A7B0, 0x28A7B4, 'saves'), (0x28A7D0, 0x28A7D4, 'saves'),
    (0x28A8F0, 0x28A8F5, 'saves'), (0x28AA90, 0x28AA95, 'saves'), (0x28AC40, 0x28AC45, 'saves'),
    (0x175EC0, 0x175EC7, 'menus'), (0x3716D6, 0x3716DB, 'menus'),
    (0x190ACE, 0x190AD0, 'movement'), (0x1923D5, 0x1923D9, 'movement'), (0x193F9F, 0x193FA5, 'movement'), (0x2A4470, 0x2A4476, 'movement'),
    (0x2A48A0, 0x2A4930, 'movement'), (0x2A4A15, 0x2A4A1D, 'movement'), (0x2A4AA2, 0x2A4AAB, 'movement'), (0x2A4C8E, 0x2A4C94, 'movement'),
]
def owner(off):
    for a, b, g in OWNERS:
        if a <= off <= b:
            return g
    raise SystemExit('exe change at file offset %X has no owner - new patch? Update OWNERS.' % off)

groups = {k: [] for k in ('common', 'widescreen', 'menus', 'movement', 'saves')}
for s, l in runs(stock, final[:len(stock)]):
    cur_g, cur_s = None, None
    for i in range(s, s + l):
        if i in range(ASPECT_3D, ASPECT_3D + 4):
            g = None
        else:
            g = owner(i)
        if g != cur_g:
            if cur_g:
                groups[cur_g].append((cur_s, final[cur_s:i]))
            cur_g, cur_s = g, i
    if cur_g:
        groups[cur_g].append((cur_s, final[cur_s:s + l]))
for name, (off, size, va) in SEC.items():
    body = final[off:off + size]
    for s, l in runs(bytes(len(body)), body, off, gap=16):
        if s <= ASPECT_2D < s + l:       # split the float out
            if ASPECT_2D > s:
                groups[name].append((s, final[s:ASPECT_2D]))
            if s + l > ASPECT_2D + 4:
                groups[name].append((ASPECT_2D + 4, final[ASPECT_2D + 4:s + l]))
        else:
            groups[name].append((s, final[s:s + l]))
for g in groups.values():
    g.sort()

# everything past the sections is zero
tail_used = max(o + len(b) for g in groups.values() for o, b in g)
assert not any(final[tail_used:]) or tail_used <= 0x4FA000

# the widescreen group must be exactly what the tested widescreen-only exe changed
ws = rd(R, 'focom_pre_avoidfix.exe')
img = bytearray(stock)
for o, b in groups['widescreen']:
    img[o:o + len(b)] = b
img[ASPECT_3D:ASPECT_3D + 4] = struct.pack('<f', 0.75)
assert bytes(img) == ws, 'widescreen group differs from the tested widescreen exe'

# independence: no option's code may reach into another option's code
RANGES = {'movement': (0x95F000, 0x9A0000), 'saves': (0x9A0000, 0x9A1000), 'menus': (0x9A1000, 0x9A2000), 'widescreen': (0x7C2D70, 0x7C3000)}
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.skipdata = True
def references(name):
    """(addr, target) for every immediate / displacement / branch target in a group's code"""
    out = []
    for o, b in groups[name]:
        if o < 0x1000:
            continue
        va = 0x400000 + o if o < 0x4B7000 else SEC[name][2] + (o - SEC[name][0])
        for ins in md.disasm(b, va):
            for tok in ins.op_str.replace('[', ' ').replace(']', ' ').replace(',', ' ').replace('+', ' ').split():
                if tok.startswith('0x'):
                    try:
                        out.append((ins.address, int(tok, 16)))
                    except ValueError:
                        pass
    return out
for name in ('movement', 'saves', 'menus', 'widescreen'):
    for addr, t in references(name):
        for other, (a, b) in RANGES.items():
            if other != name and a <= t < b:
                # a hook in .text jumping into its own section is the group's own range, so anything here is a real link
                raise SystemExit('%s code at %X refers to %s (%X)' % (name, addr, other, t))

# ---------------------------------------------------------------- patch archive
RS = rd(F, 'fcv1p1_original.rpk')
RM = rd(F, 'fcv1p1_pre_saveslots.rpk')
RP = rd(F, 'fcv1p1_pre_ai.rpk')
RF = rd(F, 'fcv1p1.rpk')
RPK_STOCK_MD5, RPK_FINAL_MD5 = md5(RS), md5(RF)

data_size = struct.unpack_from('<I', RS, 0x10)[0]
ins_at = 0x14 + data_size
grow = struct.unpack_from('<I', RF, 0x10)[0] - data_size
assert len(RF) == len(RS) + grow and RF[ins_at + grow:ins_at + grow + 0] == b''

def rungroup(a, b):
    return [(s, b[s:s + l]) for s, l in runs(a, b)]
mines = rungroup(RS, RM)
saves_r = rungroup(RM, RP)
# stock coordinates: in-place part, then the directory after the insertion
ai_flat = bytearray(RF[:ins_at]) + RF[ins_at + grow:]
assert len(ai_flat) == len(RP)
ai = rungroup(RP, bytes(ai_flat))
assert all(0x55D000 < o < 0x55F000 for o, _ in mines), 'mine patch moved'
touched = lambda g: {i for o, b in g for i in range(o, o + len(b))}
assert not (touched(mines) & touched(saves_r)) and not (touched(mines) & touched(ai)) and not (touched(saves_r) & touched(ai))

# the relocated object, as ops against the stock archive
blob = RF[ins_at:ins_at + grow]
dir_runs = [(o, b) for o, b in ai if o > ins_at]
entry = dir_runs[0][0]        # one directory entry: [u32 offset][u32 size]
assert sum(len(b) for _, b in dir_runs) == 5 and dir_runs[-1][0] + len(dir_runs[-1][1]) <= entry + 8
old_off = struct.unpack_from('<I', RS, entry)[0]
new_off = struct.unpack_from('<I', RF, entry + grow)[0]
assert new_off == ins_at, 'directory entry layout changed'
old_size = struct.unpack_from('<I', RS, entry + 4)[0]
assert struct.unpack_from('<I', RF, entry + 4 + grow)[0] == grow
src = RS[old_off:old_off + old_size]

SEED = 32
index = {}
for i in range(0, len(src) - SEED):
    index.setdefault(src[i:i + SEED], []).append(i)
ops, i, lit = [], 0, bytearray()
while i < len(blob):
    best_len, best_at = 0, 0
    for c in index.get(blob[i:i + SEED], ())[:64]:
        n = SEED
        while i + n < len(blob) and c + n < len(src) and blob[i + n] == src[c + n]:
            n += 1
        if n > best_len:
            best_len, best_at = n, c
    if best_len:
        if lit:
            ops.append(('L', bytes(lit)))
            lit = bytearray()
        ops.append(('C', old_off + best_at, best_len))
        i += best_len
    else:
        lit.append(blob[i])
        i += 1
if lit:
    ops.append(('L', bytes(lit)))
# prove the ops rebuild the object
check = bytearray()
for op in ops:
    check += RS[op[1]:op[1] + op[2]] if op[0] == 'C' else op[1]
assert bytes(check) == blob
literal_total = sum(len(op[1]) for op in ops if op[0] == 'L')

# prove the full rebuild
img = bytearray(RS)
for g in (mines, saves_r, ai):
    for o, b in g:
        img[o:o + len(b)] = b
img[ins_at:ins_at] = check
assert md5(bytes(img)) == RPK_FINAL_MD5, 'patch archive rebuild mismatch'

# ---------------------------------------------------------------- base archive
BASE_STOCK = os.path.join(F, 'forcecommand_pre_bldgs.rpk')
BASE_FINAL = os.path.join(F, 'forcecommand.rpk')
base_size = os.path.getsize(BASE_STOCK)
assert os.path.getsize(BASE_FINAL) == base_size
bld = []
h = hashlib.md5()
with open(BASE_STOCK, 'rb') as a, open(BASE_FINAL, 'rb') as b:
    pos = 0
    while True:
        x, y = a.read(1 << 22), b.read(1 << 22)
        if not x:
            break
        h.update(x)
        if x != y:
            for s, l in runs(x, y, pos):
                bld.append((s, x[s - pos:s - pos + l], y[s - pos:s - pos + l]))
        pos += len(x)
BASE_MD5 = h.hexdigest()
assert all(len(o) == len(n) for _, o, n in bld) and len(bld) == 13, 'unexpected base archive change'

# ---------------------------------------------------------------- output
def cs_run(o, b):
    return 'R(0x%X, "%s")' % (o, b.hex())
def cs_group(key, file, rs):
    return '        new Patch("%s", "%s", new[] {\n            %s,\n        }),' % (key, file, ',\n            '.join(cs_run(o, b) for o, b in rs))

lines = ['// Generated by gen.py - do not edit.', 'static class PatchData {',
         '    public const string ExeMd5 = "%s";' % EXE_STOCK_MD5,
         '    public const string ExeTestedMd5 = "%s";' % EXE_FINAL_MD5,
         '    public const string RpkMd5 = "%s";' % RPK_STOCK_MD5,
         '    public const string RpkTestedMd5 = "%s";' % RPK_FINAL_MD5,
         '    public const string BaseRpkMd5 = "%s";' % BASE_MD5,
         '    public const long BaseRpkSize = %d;' % base_size,
         '    public const int ExeFinalSize = 0x%X;' % len(final),
         '    public const int Aspect3D = 0x%X, Aspect2D = 0x%X;' % (ASPECT_3D, ASPECT_2D),
         '    public const int RpkInsertAt = 0x%X;' % ins_at,
         '    static Run R(int o, string hex) { return new Run(o, Hex(hex)); }',
         '    public static byte[] Hex(string h) { var b = new byte[h.Length / 2]; for (int i = 0; i < b.Length; i++) b[i] = System.Convert.ToByte(h.Substring(i * 2, 2), 16); return b; }',
         '    public static readonly Patch[] Groups = {']
for k in ('common', 'widescreen', 'menus', 'movement', 'saves'):
    lines.append(cs_group(k, 'exe', groups[k]))
lines.append(cs_group('mines', 'rpk', mines))
lines.append(cs_group('saves', 'rpk', saves_r))
lines.append(cs_group('ai', 'rpk', ai))
lines.append('    };')
lines.append('    // the AI unit class grew, so it moves to the end of the archive: rebuilt from your own file plus these bytes')
lines.append('    public static readonly RpkOp[] RpkInsert = {')
for op in ops:
    lines.append('        new RpkOp(0x%X, %d),' % (op[1], op[2]) if op[0] == 'C' else '        new RpkOp("%s"),' % op[1].hex())
lines.append('    };')
lines.append('    public static readonly BaseRun[] Bldgs = {')
for o, a, b in bld:
    lines.append('        new BaseRun(0x%X, "%s", "%s"),' % (o, a.hex(), b.hex()))
lines.append('    };')
lines.append('}')
with open(os.path.join(HERE, 'PatchData.cs'), 'w', newline='\r\n') as f:
    f.write('\n'.join(lines) + '\n')

os.makedirs(os.path.join(HERE, '..', 'dist'), exist_ok=True)
with open(os.path.join(HERE, '..', 'dist', 'PATCHES.txt'), 'w', newline='\r\n') as f:
    w = lambda s='': f.write(s + '\n')
    w('Star Wars: Force Commander mod patches - technical listing')
    w()
    w('focom.exe          original MD5 %s   all options (16:9) %s' % (EXE_STOCK_MD5, EXE_FINAL_MD5))
    w('fcv1p1.rpk         original MD5 %s   all options        %s' % (RPK_STOCK_MD5, RPK_FINAL_MD5))
    w('forcecommand.rpk   original MD5 %s   (patched in place, %d bytes in total)' % (BASE_MD5, sum(len(a) for _, a, _ in bld)))
    w()
    w('Offsets are file offsets. Long runs are shortened here; the installer holds them in full.')
    w('Aspect floats in focom.exe: %X = (4/3)/aspect, %X = 2 x that.' % (ASPECT_3D, ASPECT_2D))
    def dump(title, rs):
        w()
        w(title + ' (%d runs, %d bytes)' % (len(rs), sum(len(b) for _, b in rs)))
        for o, b in rs:
            w('  %08X  %5d  %s%s' % (o, len(b), b[:24].hex(), '...' if len(b) > 24 else ''))
    dump('focom.exe - section headers (any of movement / save list / menus)', groups['common'])
    dump('focom.exe - widescreen', groups['widescreen'])
    dump('focom.exe - menus (.fcmn)', groups['menus'])
    dump('focom.exe - movement (.fcmv)', groups['movement'])
    dump('focom.exe - save list (.fcsv)', groups['saves'])
    dump('fcv1p1.rpk - mines', mines)
    dump('fcv1p1.rpk - save list scripts', saves_r)
    dump('fcv1p1.rpk - AI (offsets in the original file; the directory entry moves down when the object is inserted)', ai)
    w()
    w('fcv1p1.rpk - AI unit class object inserted at %X: %d bytes, %d copied from the original object at %X, %d new'
      % (ins_at, grow, grow - literal_total, old_off, literal_total))
    w()
    w('forcecommand.rpk - early hospital + repair bay (%d runs)' % len(bld))
    for o, a, b in bld:
        w('  %08X  %s -> %s' % (o, a.hex(), b.hex()))

print('exe groups:', {k: (len(v), sum(len(b) for _, b in v)) for k, v in groups.items()})
print('rpk: mines', len(mines), 'saves', len(saves_r), 'ai', len(ai), '| insert', grow, 'bytes =', len(ops), 'ops,', literal_total, 'new bytes')
print('base:', len(bld), 'runs; stock md5', BASE_MD5)
