# Independent sparse state-vector reference (pure Python, little-endian: q0 = LSB).
import cmath, math
R2 = 1 / math.sqrt(2)
def U(t, p, l):
    c, s = math.cos(t / 2), math.sin(t / 2)
    return [[c, -cmath.exp(1j * l) * s], [cmath.exp(1j * p) * s, cmath.exp(1j * (p + l)) * c]]
def local(name, a):
    if name == 'h': return [[R2, R2], [R2, -R2]]
    if name == 'x': return [[0, 1], [1, 0]]
    if name == 'y': return [[0, -1j], [1j, 0]]
    if name == 'z': return [[1, 0], [0, -1]]
    if name == 's': return [[1, 0], [0, 1j]]
    if name == 'sdg': return [[1, 0], [0, -1j]]
    if name == 't': return [[1, 0], [0, cmath.exp(1j * math.pi / 4)]]
    if name == 'tdg': return [[1, 0], [0, cmath.exp(-1j * math.pi / 4)]]
    if name == 'i': return [[1, 0], [0, 1]]
    if name == 'sx': return [[.5 + .5j, .5 - .5j], [.5 - .5j, .5 + .5j]]
    if name == 'sxdg': return [[.5 - .5j, .5 + .5j], [.5 + .5j, .5 - .5j]]
    if name == 'rx': c, s = math.cos(a[0] / 2), math.sin(a[0] / 2); return [[c, -1j * s], [-1j * s, c]]
    if name == 'ry': c, s = math.cos(a[0] / 2), math.sin(a[0] / 2); return [[c, -s], [s, c]]
    if name == 'rz': return [[cmath.exp(-1j * a[0] / 2), 0], [0, cmath.exp(1j * a[0] / 2)]]
    if name == 'p': return [[1, 0], [0, cmath.exp(1j * a[0])]]
    if name == 'u': return U(a[0], a[1], a[2])
    raise KeyError(name)
def apply1(st, g, q, controls=()):
    out = {}; bit = 1 << q; cm = 0
    for c in controls: cm |= 1 << c
    done = set()
    for i in list(st):
        if (i & cm) != cm:
            out[i] = out.get(i, 0) + st[i]; continue
        z = i & ~bit
        if z in done: continue
        done.add(z); a, b = st.get(z, 0), st.get(z | bit, 0)
        out[z] = out.get(z, 0) + g[0][0] * a + g[0][1] * b
        out[z | bit] = out.get(z | bit, 0) + g[1][0] * a + g[1][1] * b
    return {k: v for k, v in out.items() if abs(v) > 1e-15}
PAULI = {'x': [[0, 1], [1, 0]], 'y': [[0, -1j], [1j, 0]], 'z': [[1, 0], [0, -1]]}
def pauli2(st, name, th, qa, qb):
    pa, pb = {'rxx': ('x', 'x'), 'ryy': ('y', 'y'), 'rzz': ('z', 'z'), 'rzx': ('z', 'x')}[name]
    A, B = PAULI[pa], PAULI[pb]; c, s = math.cos(th / 2), math.sin(th / 2); out = {}
    for i, v in st.items():
        out[i] = out.get(i, 0) + c * v
        ba, bb = (i >> qa) & 1, (i >> qb) & 1
        for na in (0, 1):
            for nb in (0, 1):
                coef = A[na][ba] * B[nb][bb]
                if coef == 0: continue
                j = (i & ~(1 << qa) & ~(1 << qb)) | (na << qa) | (nb << qb)
                out[j] = out.get(j, 0) + (-1j * s) * coef * v
    return {k: v for k, v in out.items() if abs(v) > 1e-15}
def run(n, init, gates, limit=1 << 17):
    st = {int(init): 1 + 0j}
    for g in gates:
        name = g['name']; a = [float(x) for x in (g.get('anglesExact') or g.get('angles') or [])]; qs = g['qubits']
        if g.get('all'): qs = list(range(n))
        if name in ('rxx', 'ryy', 'rzz', 'rzx'): st = pauli2(st, name, a[0], qs[0], qs[1])
        elif name == 'swap':
            st = {((i & ~(1 << qs[0]) & ~(1 << qs[1])) | (((i >> qs[0]) & 1) << qs[1]) | (((i >> qs[1]) & 1) << qs[0])): v for i, v in st.items()}
        elif name == 'cswap':
            c, x, y = qs; st = {(((i & ~(1 << x) & ~(1 << y)) | (((i >> x) & 1) << y) | (((i >> y) & 1) << x)) if (i >> c) & 1 else i): v for i, v in st.items()}
        elif name == 'ccx': st = apply1(st, local('x', []), qs[2], qs[:2])
        elif name == 'cu':
            m = U(a[0], a[1], a[2]); ph = cmath.exp(1j * a[3]); st = apply1(st, [[m[0][0] * ph, m[0][1] * ph], [m[1][0] * ph, m[1][1] * ph]], qs[1], qs[:1])
        elif name.startswith('c') and len(qs) == 2: st = apply1(st, local(name[1:], a), qs[1], qs[:1])
        else:
            for q in qs: st = apply1(st, local(name, a), q)
        if len(st) > limit: return None
    return st
def distribution(n, st, basis):
    if basis != 'z':
        pre = [local('sdg', []), local('h', [])] if basis == 'y' else [local('h', [])]
        for q in range(n):
            for g in pre: st = apply1(st, g, q)
            if len(st) > (1 << 17): return None
    return {format(i, '0%db' % n): abs(v) ** 2 for i, v in st.items() if abs(v) ** 2 > 1e-14}
