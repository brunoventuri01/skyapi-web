import struct,zlib,hashlib,json
from pathlib import Path
base=Path(__file__).resolve().parent.parent
p=base/'release/SkyAPI.exe';data=p.read_bytes()
signature=bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae')
pos=data.index(signature);offset=struct.unpack_from('<q',data,pos-8)[0];at=offset
major,minor,count=struct.unpack_from('<IIi',data,at);at+=12
def string():
 global at
 length=0;shift=0
 while True:
  n=data[at];at+=1;length|=(n&127)<<shift
  if n<128:break
  shift+=7
 value=data[at:at+length].decode();at+=length;return value
bundle_id=string();deps=struct.unpack_from('<qq',data,at);at+=16;runtime=struct.unpack_from('<qq',data,at);at+=16;flags=struct.unpack_from('<Q',data,at)[0];at+=8
assert major==6 and count>400 and flags&1
entries={}
for i in range(count):
 off,size,compressed=struct.unpack_from('<qqq',data,at);at+=24;kind=data[at];at+=1;name=string()
 raw=data[off:off+(compressed or size)]
 actual=zlib.decompress(raw,-15) if compressed else raw
 assert len(actual)==size,name
 assert name not in entries,name
 assert actual==(base/'build/stage'/name).read_bytes(),name
 entries[name]=actual
for name in ['SkyAPI.dll','SkyAPI.Core.dll','SkyAPI.runtimeconfig.json','SkyAPI.deps.json','coreclr.dll','PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','wpfgfx_cor3.dll','System.Private.CoreLib.dll']:
 assert name in entries,name
runtime_config=json.loads(entries['SkyAPI.runtimeconfig.json']);assert 'framework' not in runtime_config['runtimeOptions'];assert 'frameworks' not in runtime_config['runtimeOptions']
j=json.loads(entries['SkyAPI.deps.json']);target=j['targets'][j['runtimeTarget']['name']]
for lib in target.values():
 for category in ['runtime','native','resources']:
  for name in lib.get(category,{}):assert name in entries or name in ('hostfxr.dll','hostpolicy.dll'),'Dependência ausente: '+name
assert b'asInvoker' in data[:offset]
pe=struct.unpack_from('<I',data,0x3c)[0];assert data[pe:pe+4]==b'PE\0\0';assert struct.unpack_from('<H',data,pe+4)[0]==0x8664
subsystem=struct.unpack_from('<H',data,pe+24+68)[0];assert subsystem==2
report=f'''Verificação estrutural do executável
- PE Windows x64, interface gráfica: OK
- Manifesto asInvoker (sem administrador): OK
- Bundle .NET versão {major}.{minor}: OK
- {count} componentes incorporados, descomprimidos e comparados byte a byte: OK
- Todas as dependências declaradas presentes: OK
- Runtime incluído, sem instalação separada do .NET: OK
- SHA-256: {hashlib.sha256(data).hexdigest()}
- Tamanho: {len(data):,} bytes

Esta verificação não substitui abrir o aplicativo no Windows.
'''
print(report);(base/'build/package-verification.txt').write_text(report)
