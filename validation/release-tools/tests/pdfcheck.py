import sys, re, zlib, json, os, hashlib
def pdf_info(path):
    b = open(path,'rb').read()
    info = {'bytes': len(b), 'header': b[:5]==b'%PDF-', 'eof': b.rstrip().endswith(b'%%EOF'), 'startxref': b'startxref' in b[-2048:]}
    info['pages'] = len(re.findall(rb'/Type\s*/Page(?![s\w])', b))
    texts = []
    for m in re.finditer(rb'stream\r?\n(.*?)\r?\nendstream', b, re.S):
        data = m.group(1)
        try: data = zlib.decompress(data)
        except Exception: pass
        for t in re.findall(rb'\((.*?)(?<!\\)\)\s*Tj', data, re.S): texts.append(t)
        for arr in re.findall(rb'\[(.*?)\]\s*TJ', data, re.S):
            texts.append(b''.join(re.findall(rb'\((.*?)(?<!\\)\)', arr, re.S)))
        for h in re.findall(rb'<([0-9A-Fa-f]+)>\s*Tj', data):
            try: texts.append(bytes.fromhex(h.decode()))
            except Exception: pass
    txt = b' '.join(texts).decode('latin-1', 'replace').replace('\\(', '(').replace('\\)', ')')
    info['textChars'] = len(txt)
    return info, txt
if __name__ == '__main__':
    i, t = pdf_info(sys.argv[1]); print(json.dumps(i)); print(t[:1500])
