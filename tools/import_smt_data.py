"""Convert SMT XML snapshots to portable JSON. Usage: python3 tools/import_smt_data.py /path/to/SMT"""
import json, pathlib, subprocess, sys, xml.etree.ElementTree as ET
root = pathlib.Path(sys.argv[1])
out = pathlib.Path(__file__).resolve().parents[1] / 'data/universe.json'
systems = []
for s in ET.parse(root / 'EVEData/data/Systems.dat').getroot():
    systems.append(dict(id=int(s.findtext('ID')), name=s.findtext('Name'), region=s.findtext('Region'),
        actualX=float(s.findtext('ActualX')), actualY=float(s.findtext('ActualY')), actualZ=float(s.findtext('ActualZ')),
        security=float(s.findtext('TrueSec')), x=float(s.findtext('UniverseX')), y=float(s.findtext('UniverseY')),
        station=s.findtext('HasNPCStation') == 'true', jumps=[j.text for j in s.findall('Jumps/string')]))
known = {s['name'] for s in systems}
regions = []
for r in ET.parse(root / 'EVEData/data/MapLayout.dat').getroot():
    nodes = [dict(name=m.findtext('Name'), x=float(m.findtext('Layout/X')), y=float(m.findtext('Layout/Y')),
                  outside=m.findtext('OutOfRegion') == 'true')
             for m in r.findall('MapSystems/item/value/MapSystem') if m.findtext('Name') in known]
    if nodes:
        regions.append(dict(name=r.findtext('Name'), faction=r.findtext('Faction') or '', nodes=nodes))
sha = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
data = dict(source='https://github.com/Slazanger/SMT', commit=sha, systems=systems, regions=regions)
out.write_text(json.dumps(data, separators=(',', ':')))
print(f'{len(systems)} systems, {len(regions)} regional layouts → {out}')

ships = {i.findtext('key/string'): i.findtext('value/string') for i in ET.parse(root / 'EVEData/data/ShipTypes.dat').getroot()}
out.with_name('ship-types.json').write_text(json.dumps(ships, separators=(',', ':')))
