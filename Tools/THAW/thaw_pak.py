"""Listet und entpackt THAW-PC-Archive (*.pak.wpc, Daten ggf. in *.pab.wpc)."""
import os
import struct
import sys
import zlib


def qbkey(s):
    """Neversoft-Pruefsumme (CRC32 ohne Abschluss-XOR, Kleinbuchstaben, Backslashes)."""
    s = s.lower().replace('/', '\\')
    return zlib.crc32(s.encode('latin-1')) ^ 0xFFFFFFFF


EXT = {qbkey(e): e for e in ['.ske', '.skin', '.tex', '.mdl', '.scn', '.col', '.qb', '.anm', '.anim', '.img', '.last',
                             '.dat', '.cam', '.fam', '.stex', '.wav', '.pfx', '.ska', '.clt', '.nqb', '.oqb', '.mqb',
                             '.hkc', '.trg', '.geom', '.rnb', '.mcol', '.cas', '.wpc', '.xbx', '.ps2', '.ffd', '.pimg']}


def entries(pak):
    pab = pak.replace('.pak.', '.pab.')
    data = open(pak, 'rb').read()
    body = open(pab, 'rb').read() if os.path.exists(pab) else data
    o = 0
    out = []
    while o + 32 <= len(data):
        ext, off, size, pakname, full, short, unk, flags = struct.unpack_from('<8I', data, o)
        name = None
        hdr = 32
        if flags & 0x20:
            name = data[o + 32:o + 32 + 160].split(b'\0')[0].decode('latin-1')
            hdr += 160
        e = EXT.get(ext, hex(ext))
        if e == '.last':
            break
        start = o + off if body is data else o + off - len(data)
        out.append(dict(ext=e, start=start, size=size, full=full, short=short, name=name, flags=flags, src=body))
        o += hdr
    return out


def read(entry):
    return entry['src'][entry['start']:entry['start'] + entry['size']]


if __name__ == '__main__':
    sys.stdout.reconfigure(errors='replace')
    for e in entries(sys.argv[1]):
        print(f"{e['ext']:>8} start={e['start']:#x} size={e['size']:>8} full={e['full']:#010x} short={e['short']:#010x} name={e['name']}")
