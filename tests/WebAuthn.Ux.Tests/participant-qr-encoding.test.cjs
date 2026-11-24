'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),path=require('node:path');
const {execFileSync}=require('node:child_process');

// Decoder/RS syndrome check uses specification tables and log/exp arithmetic,
// independently of the C# encoder's polynomial division and module reservation.
function decode(sample){
  const v=sample.Version,n=v*4+17,a=sample.Rows.map(row=>[...row].map(c=>c==='1'));
  assert.equal(a.length,n);assert.ok(a.every(row=>row.length===n));
  let format=0;
  const formatPoints=[...Array.from({length:6},(_,i)=>[8,i]),[8,7],[8,8],[7,8],...Array.from({length:6},(_,i)=>[5-i,8])];
  formatPoints.forEach(([x,y],i)=>{if(a[y][x])format|=1<<i;});
  assert.equal(format,0x77c4,'Level L, mask 0 format with BCH protection');
  for(const [cx,cy] of [[3,3],[n-4,3],[3,n-4]])for(let y=-3;y<=3;y++)for(let x=-3;x<=3;x++)
    assert.equal(a[cy+y][cx+x],Math.max(Math.abs(x),Math.abs(y))!==2,'Standard finder');
  const positions=[[],[],[6,18],[6,22],[6,26],[6,30],[6,34],[6,22,38],[6,24,42],[6,26,46],[6,28,50]][v];
  for(const px of positions)for(const py of positions){
    if((px===6&&(py===6||py===n-7))||(px===n-7&&py===6))continue;
    for(let y=-2;y<=2;y++)for(let x=-2;x<=2;x++)assert.equal(a[py+y][px+x],Math.max(Math.abs(x),Math.abs(y))!==1,'Standard alignment pattern');
  }
  for(let i=8;i<n-8;i++){assert.equal(a[6][i],i%2===0);assert.equal(a[i][6],i%2===0);}
  assert.equal(a[n-8][8],true,'Fixed dark module');
  if(v>=7){
    let remainder=v<<12;for(let i=17;i>=12;i--)if(remainder&(1<<i))remainder^=0x1f25<<(i-12);
    const expected=(v<<12)|remainder;
    for(let i=0;i<18;i++){assert.equal(a[Math.floor(i/3)][n-11+i%3],Boolean(expected&(1<<i)));assert.equal(a[n-11+i%3][Math.floor(i/3)],Boolean(expected&(1<<i)));}
  }
  const reserved=(x,y)=>{
    if(x===6||y===6||(x<9&&y<9)||(x>=n-8&&y<9)||(x<9&&y>=n-8))return true;
    if(v>=7&&((x>=n-11&&x<n-8&&y<6)||(y>=n-11&&y<n-8&&x<6)))return true;
    for(const px of positions)for(const py of positions){
      if((px===6&&(py===6||py===n-7))||(px===n-7&&py===6))continue;
      if(Math.abs(x-px)<=2&&Math.abs(y-py)<=2)return true;
    }
    return false;
  };
  const bits=[];let up=true;
  for(let x=n-1;x>0;x-=2){if(x===6)x--;for(let r=0;r<n;r++){
    const y=up?n-1-r:r;for(const dx of [0,1])if(!reserved(x-dx,y))bits.push(Number(a[y][x-dx])^Number(((x-dx+y)&1)===0));
  }up=!up;}
  const spec=[null,[[1,26,19]],[[1,44,34]],[[1,70,55]],[[1,100,80]],[[1,134,108]],[[2,86,68]],[[2,98,78]],[[2,121,97]],[[2,146,116]],[[2,86,68],[2,87,69]]][v];
  const blocks=spec.flatMap(([count,total,data])=>Array.from({length:count},()=>({total,data,words:[]})));
  const words=Array.from({length:Math.floor(bits.length/8)},(_,i)=>bits.slice(i*8,i*8+8).reduce((p,b)=>(p<<1)|b,0));
  assert.equal(words.length,blocks.reduce((sum,b)=>sum+b.total,0));
  assert.ok(bits.slice(words.length*8).every(b=>b===0),'Zero remainder bits');
  let offset=0;
  for(let i=0;i<Math.max(...blocks.map(b=>b.data));i++)for(const block of blocks)if(i<block.data)block.words.push(words[offset++]);
  for(let i=0;i<blocks[0].total-blocks[0].data;i++)for(const block of blocks)block.words.push(words[offset++]);
  const exp=[],log=[];let z=1;for(let i=0;i<255;i++){exp[i]=z;log[z]=i;z<<=1;if(z&256)z^=0x11d;}
  const multiply=(x,y)=>x&&y?exp[(log[x]+log[y])%255]:0;
  for(const block of blocks)for(let root=0;root<block.total-block.data;root++){
    const syndrome=block.words.reduce((sum,b)=>multiply(sum,exp[root])^b,0);assert.equal(syndrome,0,'Reed-Solomon syndrome');
  }
  const data=blocks.flatMap(b=>b.words.slice(0,b.data));let bit=0;
  const take=count=>{let value=0;while(count--)value=(value<<1)|((data[bit>>3]>>(7-(bit++&7)))&1);return value;};
  assert.equal(take(4),4,'Byte mode');const length=take(v<10?8:16);
  const text=Buffer.from(Array.from({length},()=>take(8))).toString('ascii');
  assert.equal(text,sample.Text,'Decoded payload exactly equals supplied URL/text');
  assert.match(sample.Svg,new RegExp(`viewBox="0 0 ${n+8} ${n+8}"`));
  const dark=new Set();
  for(const m of sample.Svg.matchAll(/M(\d+),(\d+)h1v1h-1z/g)){
    assert.ok(+m[1]>=4&&+m[2]>=4&&+m[1]<n+4&&+m[2]<n+4,'Four-module quiet border');dark.add(`${+m[1]-4},${+m[2]-4}`);
  }
  for(let y=0;y<n;y++)for(let x=0;x<n;x++)assert.equal(dark.has(`${x},${y}`),a[y][x],'SVG exactly renders the decoded matrix');
  return text;
}
test('C# QR versions 1–10 decode exactly, with valid format and Reed-Solomon blocks',t=>{
  let output;
  try{output=execFileSync('pwsh',['-NoLogo','-NoProfile','-NonInteractive','-File',path.join(__dirname,'Export-ParticipantQr.ps1')],{encoding:'utf8',timeout:45000,maxBuffer:2000000});}
  catch(error){if(error.code==='ENOENT'){t.skip('Standalone encoding fixture requires PowerShell 7; run the .NET helper checks as well');return;}throw error;}
  const fixture=JSON.parse(output);fixture.Samples.forEach(decode);
  assert.deepEqual(fixture.Samples.slice(0,10).map(s=>s.Version),[1,2,3,4,5,6,7,8,9,10]);
  const events=fixture.Samples.slice(-2).map(s=>s.Text);
  assert.deepEqual(events,['00000000-0000-0000-0000-000000000401','00000000-0000-0000-0000-000000000402'].map(id=>'https://cybertruck.example.test/portal/Index?eventKind=experience&eventId='+id));
  assert.equal(fixture.Rejected,4);assert.equal(fixture.LaptopOnly,true);assert.equal(fixture.PublicOnly,false);
  assert.deepEqual(fixture.Policy,{Initial:true,Changed:true,Closed:true,Disconnected:true});
});

test('configured visitor URL wins over management path and preserves RP-ID and localhost defaults',()=>{
 const fixture=JSON.parse(execFileSync('pwsh',['-NoLogo','-NoProfile','-NonInteractive','-File',path.join(__dirname,'Export-VisitorLinks.ps1')],{encoding:'utf8',timeout:45000,maxBuffer:2000000}));
 fixture.Samples.forEach(decode);
 assert.deepEqual(fixture.Samples.map(s=>s.Text),['00000000-0000-0000-0000-000000000401','00000000-0000-0000-0000-000000000402'].map(id=>'https://cybertruck.example.test/portal/Index?eventKind=prize&eventId='+id));
 assert.equal(fixture.Rejected,6);assert.equal(fixture.LocalOnly,true);
 assert.equal(fixture.Local,'https://localhost:7243/Index?eventKind=prize&eventId=00000000-0000-0000-0000-000000000401');
 assert.equal(fixture.Fallback,'https://cybertruck.example.test/existing-prefix/Index?eventKind=prize&eventId=00000000-0000-0000-0000-000000000401');
});
