import argparse
import collections
import json
import math
from html.parser import HTMLParser
from pathlib import Path
import re
import subprocess


class Document(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=False)
        self.scripts = []
        self.ids = []
        self.current = None

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if 'id' in attrs:
            self.ids.append(attrs['id'])
        if tag.lower() == 'script':
            self.current = {'attrs': attrs, 'text': ''}

    def handle_data(self, data):
        if self.current is not None:
            self.current['text'] += data

    def handle_endtag(self, tag):
        if tag.lower() == 'script' and self.current is not None:
            self.scripts.append(self.current)
            self.current = None


def nonfinite(value):
    if isinstance(value, float):
        return not math.isfinite(value)
    if isinstance(value, dict):
        return any(nonfinite(item) for item in value.values())
    if isinstance(value, list):
        return any(nonfinite(item) for item in value)
    return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', required=True)
    parser.add_argument('--node', required=True)
    parser.add_argument('--out', required=True)
    args = parser.parse_args()
    root = Path(args.root)
    output = Path(args.out)
    temp = output.parent / 'syntax-inline'
    temp.mkdir(exist_ok=True)
    report = {'root': str(root), 'javascript': {'fileCount': 0, 'inlineCount': 0, 'passed': [], 'failed': []}, 'json': {'count': 0, 'valid': [], 'malformed': [], 'nonfinite': []}, 'html': {'count': 0, 'documents': []}}
    for path in sorted(root.rglob('*.js')):
        report['javascript']['fileCount'] += 1
        checked = subprocess.run([args.node, '--check', str(path)], capture_output=True, text=True, encoding='utf-8')
        report['javascript']['passed' if checked.returncode == 0 else 'failed'].append(str(path))
    for index, path in enumerate(sorted(root.rglob('*.html'))):
        report['html']['count'] += 1
        document = Document()
        document.feed(path.read_text(encoding='utf-8-sig'))
        item = {'path': str(path), 'inlineScripts': 0, 'dataScripts': 0, 'externalScripts': []}
        for number, script in enumerate(document.scripts):
            attributes = script['attrs']
            if attributes.get('src'):
                item['externalScripts'].append(attributes['src'])
                continue
            kind = attributes.get('type', '').lower().strip()
            if kind not in ('', 'module', 'text/javascript', 'application/javascript'):
                item['dataScripts'] += 1
                continue
            item['inlineScripts'] += 1
            report['javascript']['inlineCount'] += 1
            script_path = temp / f'{index:03d}-{number:03d}.{"mjs" if kind == "module" else "js"}'
            script_path.write_text(script['text'], encoding='utf-8')
            checked = subprocess.run([args.node, '--check', str(script_path)], capture_output=True, text=True, encoding='utf-8')
            report['javascript']['passed' if checked.returncode == 0 else 'failed'].append(f'{path}#inline-script-{number + 1}')
        report['html']['documents'].append(item)
        if path.name == 'QUANTUM_TERMINAL.html':
            text = '\n'.join(script['text'] for script in document.scripts)
            literal_ids = set(re.findall(r'''\$\s*\(\s*(['"])(.*?)\1\s*\)''', text))
            requested_ids = sorted({name for _, name in literal_ids})
            counts = collections.Counter(document.ids)
            report['terminalDom'] = {'staticIdCount': len(document.ids), 'literalLookupCount': len(requested_ids), 'unresolvedLiteralIds': [name for name in requested_ids if name not in counts], 'duplicateStaticIds': sorted(name for name, count in counts.items() if count > 1)}
            helper_ids = sorted(set(re.findall(r'''\b(?:text|commandButton)\s*\(\s*['"]([^'"]+)['"]''', text)))
            report['terminalDom']['literalHelperLookupCount'] = len(helper_ids)
            report['terminalDom']['unresolvedLiteralHelperIds'] = [name for name in helper_ids if name not in counts]
            regression = subprocess.run([args.node, str(Path(__file__).with_name('transport_pdf_export_check.js')), str(path)], capture_output=True, text=True, encoding='utf-8')
            report['sourcePdfDownloadRegression'] = json.loads(regression.stdout) if regression.returncode == 0 else {'passed': False, 'test': str(Path(__file__).with_name('transport_pdf_export_check.js'))}
    for path in sorted(root.rglob('*.json')):
        report['json']['count'] += 1
        try:
            data = json.loads(path.read_text(encoding='utf-8-sig'))
        except (json.JSONDecodeError, UnicodeError):
            report['json']['malformed'].append(str(path))
        else:
            report['json']['valid'].append(str(path))
            if nonfinite(data):
                report['json']['nonfinite'].append(str(path))
    report['summary'] = {'javascriptPassed': len(report['javascript']['passed']), 'javascriptFailed': len(report['javascript']['failed']), 'jsonValid': len(report['json']['valid']), 'jsonMalformed': len(report['json']['malformed']), 'jsonNonfinite': len(report['json']['nonfinite']), 'terminalUnresolvedLiteralIds': report.get('terminalDom', {}).get('unresolvedLiteralIds', [])}
    output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(json.dumps(report['summary'], indent=2))


if __name__ == '__main__':
    main()
