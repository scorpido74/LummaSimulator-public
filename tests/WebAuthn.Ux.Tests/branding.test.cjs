const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const ico=fs.readFileSync(path.join(root,'LummaSimulator.App/Assets/LummaSimulator.ico'));
function frames(){
    const count=ico.readUInt16LE(4);return Array.from({length:count},(_,i)=>{const o=6+i*16;return {size:ico[o]||256,data:ico.subarray(ico.readUInt32LE(o+12),ico.readUInt32LE(o+12)+ico.readUInt32LE(o+8))};});
}
function catalog(file){const xml=read(file);return new Map([...xml.matchAll(/<data name="([^"]+)"[^>]*><value>([\s\S]*?)<\/value><\/data>/g)].map(m=>[m[1],m[2]]));}
test('neutral icon contains valid 16,32,48,256 PNG frames and a Windows ICO directory',()=>{
    assert.equal(ico.readUInt16LE(0),0);assert.equal(ico.readUInt16LE(2),1);
    assert.deepEqual(frames().map(f=>f.size),[16,32,48,256]);
    for(const f of frames()){assert.deepEqual([...f.data.subarray(0,8)],[137,80,78,71,13,10,26,10]);assert.equal(f.data.readUInt32BE(16),f.size);assert.equal(f.data.readUInt32BE(20),f.size);}
});
test('executable and WPF window use the same embedded icon, without launcher title overrides',()=>{
    assert.match(read('LummaSimulator.App/LummaSimulator.App.csproj'),/<ApplicationIcon>Assets\\LummaSimulator.ico<\/ApplicationIcon>/);
    assert.match(read('LummaSimulator.App/LummaSimulator.App.csproj'),/<Resource Include="Assets\\LummaSimulator.ico"/);
    assert.match(read('LummaSimulator.App/MainWindow.xaml'),/Icon="Assets\/LummaSimulator.ico"/);
    assert.match(read('packaging/Build-EventEdition.ps1'),/dotnet|Invoke-Dotnet/);
    assert.doesNotMatch(read('packaging/Start-LummaDev.ps1'),/SetWindowLong|WM_SETICON|AppUserModelID/);
});
for(const [name,variable] of [['build','LUMMA_TEST_APP_EXE'],['published Event Edition','LUMMA_TEST_PUBLISHED_APP_EXE']]){
    test(`${name} PE contains every exact neutral icon frame`,{skip:!process.env[variable]},()=>{
        const executable=fs.readFileSync(process.env[variable]);assert.equal(executable.toString('ascii',0,2),'MZ');
        for(const f of frames())assert.notEqual(executable.indexOf(f.data),-1,'PE is missing icon frame '+f.size);
    });
}
test('desktop Dutch/English catalogs have matching keys, placeholders and all six phase names',()=>{
    const nl=catalog('LummaSimulator.App/DesktopText.resx'),en=catalog('LummaSimulator.App/DesktopText.en-GB.resx');
    assert.deepEqual([...nl.keys()].sort(),[...en.keys()].sort());assert.ok(nl.size>200);
    for(const [key,value] of nl)assert.deepEqual([...(value.match(/\{\d+\}/g)||[])].sort(),[...(en.get(key).match(/\{\d+\}/g)||[])].sort(),key);
    for(const phase of ['User Execution','System Information Discovery','Browser Information Discovery','Credentials from Web Browsers','Local Data Staging','Exfiltration Over C2 Channel']){
        const entry=[...en].find(([,v])=>v===phase);assert.ok(entry,phase);assert.notEqual(nl.get(entry[0]),phase);
    }
});
test('every desktop text resource binding resolves in both catalogs; phase and status rendering refresh on language',()=>{
    const xaml=read('LummaSimulator.App/MainWindow.xaml');const en=catalog('LummaSimulator.App/DesktopText.en-GB.resx');
    for(const match of xaml.matchAll(/(?:\[|ConverterParameter=")((?:text|format)\.[^\]" ]+)/g))assert.ok(en.has(match[1]),match[1]);
    assert.match(xaml,/Binding Path="Language" Source="\{x:Static local:DesktopLanguage.Current\}"/);
    assert.match(xaml,/Click="OnDutch"/);assert.match(xaml,/Click="OnEnglish"/);
    assert.match(read('LummaSimulator.App/DesktopLanguage.cs'),/"Item\[\]"/);
});
test('all visitor ClickFix keys exist in Dutch and English including recovery and completion',()=>{
    const nl=catalog('LummaSimulator.Web/Resources/Localization/UiText.nl-NL.resx'),en=catalog('LummaSimulator.Web/Resources/Localization/UiText.en-GB.resx');
    const sources=read('LummaSimulator.Web/Pages/ClickFix.cshtml')+read('LummaSimulator.Web/Pages/Demo.cshtml')+read('LummaSimulator.Web/wwwroot/js/clickfix.js');
    const protocolNames=new Set(['ClickFixModel','ClickFixFollowSimulated','ClickFixStopSelected']);
    const keys=new Set([...sources.matchAll(/\b(?:ClickFix|Cf)[A-Za-z]+\b/g)].map(m=>m[0]).filter(k=>!protocolNames.has(k)));
    for(const key of keys){assert.ok(nl.has(key),key);assert.ok(en.has(key),key);}
});
test('branding is local, configurable and uses an encoded text fallback with responsive containment',()=>{
    const layout=read('LummaSimulator.Web/Pages/Shared/_Layout.cshtml');
    assert.match(layout,/@Presentation.Current.Organization/);assert.match(layout,/reseller-fallback/);
    assert.doesNotMatch(layout,/Html.Raw\(.*OrganizationName|https?:\/\/.*logo/);
    assert.match(read('shared/BrandingAssets.cs'),/ReparsePoint/);assert.match(read('shared/BrandingAssets.cs'),/IsUnc/);
    assert.match(read('LummaSimulator.Web/wwwroot/css/site.css'),/\.reseller-logo\s*\{\s*object-fit:contain/);
    assert.match(read('LummaSimulator.App/MainWindow.xaml'),/Stretch="Uniform"/);
});

test('new sign-in, route settings and completion strings are present in both languages',()=>{
    const nl=catalog('LummaSimulator.Web/Resources/Localization/UiText.nl-NL.resx'),en=catalog('LummaSimulator.Web/Resources/Localization/UiText.en-GB.resx');
    const sources=['Pages/SignedIn.cshtml','Pages/ClickFixComplete.cshtml','Pages/Presenter/Events/Index.cshtml','wwwroot/js/clickfix.js'].map(p=>read('LummaSimulator.Web/'+p)).join('\n');
    const keys=new Set([...sources.matchAll(/["']((?:Exercise|LoginHelp_|LoginOptions|LoginRoutes|SignedInTitle)[A-Za-z_]*)["']/g)].map(m=>m[1]));
    for(const key of keys){assert.ok(nl.has(key),key);assert.ok(en.has(key),key);}
});
