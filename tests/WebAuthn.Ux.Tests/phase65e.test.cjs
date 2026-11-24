'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const read=file=>fs.readFileSync(path.join(__dirname,'../..',file),'utf8');
function memory(t){let DatabaseSync;try{({DatabaseSync}=require('node:sqlite'));}catch{t.skip('Node SQLite unavailable; run C# phase65e checks');return null;}
  const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');
  const legacy=read('LummaSimulator.Web/Services/Prizes/PrizeMigration.cs').split('Dictionary<string,string> Legacy = new()')[1].split('\n    };')[0];
  for(const m of legacy.matchAll(/\["table:([^"]+)"\]\s*=\s*("(?:\\.|[^"\\])*")/g))db.exec(JSON.parse(m[2]));
  const prize=read('LummaSimulator.Web/Services/Prizes/PrizeMigration.cs').match(/string\[\] Statements\s*=\s*\[([\s\S]*?)\r?\n\s*\];/)[1];
  for(const m of prize.matchAll(/"(?:\\.|[^"\\])*"/g))db.exec(JSON.parse(m[0]));
  db.exec(read('LummaSimulator.Web/Services/EventAccessMigration.cs').split('internal const string SchemaSql="""')[1].split('"""')[0]);return db;
}
test('B1: rotation validates its server context and default public creation is always unassigned',t=>{
  const db=memory(t);if(!db)return;
  try{
    const source=read('LummaSimulator.Web/Services/DemoService.cs');
    const sql=source.match(/"""\s*(SELECT x.Kind,x.EventId FROM PresenterDemoContexts x[\s\S]*?)"""/)[1];
    const a='00000000-0000-0000-0000-000000000301',b='00000000-0000-0000-0000-000000000302',open='00000000-0000-0000-0000-000000000004';
    db.exec("INSERT INTO PresenterAccounts(Id,Login,Name,Role,Handle,Created,Active,Pending,Activated) VALUES('owner','owner','Synthetic','Presenter',X'01',1,1,0,1); INSERT INTO PresenterKeys(Id,Ref,AccountId,PublicKey,Counter,BackupEligible,BackedUp,Name,Registered) VALUES('key','ref','owner',X'01',1,0,0,'Synthetic',1)");
    for(const id of [a,b,open]){db.prepare("INSERT INTO ExperienceEvents(Id,Name,StartedAt,Status) VALUES(?,'Synthetic',1,?)").run(id,id===open?'Imported':'Active');db.prepare("INSERT INTO PresenterEventScopes VALUES('experience',?,'Synthetic',?)").run(id,id===open?null:'owner');}
    for(const [id,event] of [['previous-a',a],['previous-b',b],['public',open]]){
      db.prepare("INSERT INTO Demos(Id,DemoRunId,Name,UserHandle,SessionHash,Route,Created,LastSeen) VALUES(?,?,?,?,?,'laptop',1,1)").run(id,id,id,Buffer.from(id),id);
      db.prepare("INSERT INTO PresenterDemoContexts VALUES(?,'experience',?)").run(id,event);
      const context=db.prepare(sql).get({$previous:id,$open:open,$edition:null});assert.equal(context.EventId,event);
    }
    db.prepare("UPDATE ExperienceEvents SET Status='Closed' WHERE Id=?").run(a);
    assert.equal(db.prepare(sql).get({$previous:'previous-a',$open:open,$edition:null}),undefined);
    assert.equal(db.prepare(sql).get({$previous:'nonexistent',$open:open,$edition:null}),undefined);
    assert.match(source,/string contextKind="experience",contextId=ExperienceStatistics.UnassignedEvent/);
    assert.doesNotMatch(source,/CASE WHEN \(SELECT COUNT\(\*\) FROM ExperienceEvents/);
    assert.match(source,/DELETE FROM PresenterMonitorCapabilities WHERE DemoId=\$previous/);
    assert.match(source,/edition\.AccessAsync\(false/);
    const handler=read('LummaSimulator.Web/Pages/Demo.cshtml.cs');
    assert.match(handler,/webAuthn\.Origins\.Contains\(Request.Headers.Origin/);
    assert.match(handler,/Request.Query.Keys.Any/);assert.match(handler,/Request.Form.Keys.Any/);
    assert.match(handler,/demos.CreateAsync\(previous.Route, HttpContext.RequestAborted, token\)/);
    assert.match(handler,/Response.Headers\["Referrer-Policy"\]="same-origin"/);
  }finally{db.close();}
});
test('B2: transfer cleanup revokes raw/granted/ceremony state atomically and preserves activated keys',t=>{
  const db=memory(t);if(!db)return;
  try{
    const helper=read('LummaSimulator.Web/Services/PresenterEventEnrollment.cs');
    const select=helper.match(/"""\s*(SELECT DISTINCT a.Id FROM PresenterEventInvites[\s\S]*?)"""/)[1];
    const revoke=JSON.parse(helper.match(/"DELETE FROM PresenterInvitations WHERE AccountId=\$account; DELETE FROM PresenterEnrollmentGrants WHERE AccountId=\$account; DELETE FROM PresenterCeremonies WHERE AccountId=\$account AND Kind='enroll'"/)[0]);
    const id='00000000-0000-0000-0000-000000000303';
    for(const [account,active] of [['owner',true],['pending',false],['generic',false],['activated',true]]){
      db.prepare("INSERT INTO PresenterAccounts(Id,Login,Name,Role,Handle,Created,Active,Pending,Activated) VALUES(?,?,'Synthetic','Presenter',?,1,?,?,?)").run(account,account,Buffer.from(account),active?1:0,active?0:1,active?1:null);
      if(active)db.prepare("INSERT INTO PresenterKeys(Id,Ref,AccountId,PublicKey,Counter,BackupEligible,BackedUp,Name,Registered) VALUES(?,?,?,X'010203',7,0,0,'Synthetic',1)").run(account+'-key',account+'-ref',account);
      db.prepare('INSERT INTO PresenterInvitations VALUES(?,?,9999999999999,0)').run(account+'-raw',account);
      db.prepare('INSERT INTO PresenterEnrollmentGrants VALUES(?,?,9999999999999,0)').run(account+'-grant',account);
      db.prepare("INSERT INTO PresenterCeremonies VALUES(?,?,'enroll','synthetic-op','{}',9999999999999,?)").run(account+'-browser',account,account+'-grant');
    }
    db.prepare("INSERT INTO PresenterEventScopes VALUES('experience',?,'Synthetic','owner')").run(id);
    for(const account of ['pending','activated'])db.prepare("INSERT INTO PresenterEventInvites VALUES(?,'experience',?,?,'owner',9999999999999,?)").run(account+'-invite',id,account,account+'-raw');
    const snapshot=()=>JSON.stringify(['PresenterInvitations','PresenterEnrollmentGrants','PresenterCeremonies','PresenterKeys'].map(table=>db.prepare('SELECT * FROM '+table+' ORDER BY rowid').all()));
    const before=snapshot();
    function cleanup(){for(const row of db.prepare(select).all({$kind:'experience',$id:id,$target:null}))for(const statement of revoke.split(';').filter(x=>x.trim()))db.prepare(statement).run({$account:row.Id});}
    db.exec('BEGIN IMMEDIATE');cleanup();db.exec('ROLLBACK');assert.equal(snapshot(),before,'Interrupted transfer rolls back capability cleanup');
    db.exec('BEGIN IMMEDIATE');cleanup();db.prepare("DELETE FROM PresenterEventInvites WHERE Kind='experience' AND EventId=?").run(id);db.exec('COMMIT');
    for(const table of ['PresenterInvitations','PresenterEnrollmentGrants','PresenterCeremonies'])assert.equal(db.prepare('SELECT COUNT(*) n FROM '+table+' WHERE AccountId=?').get('pending').n,0);
    assert.equal(db.prepare("SELECT COUNT(*) n FROM PresenterInvitations WHERE AccountId='generic'").get().n,1);
    assert.equal(db.prepare("SELECT Counter FROM PresenterKeys WHERE AccountId='activated'").get().Counter,7);
    const auth=read('LummaSimulator.Web/Services/PresenterAuthentication.cs');
    assert.equal([...auth.matchAll(/await EventEnrollmentValid\(c,tx,/g)].length,3,'Raw redemption, options and final credential commit all revalidate provenance');
    assert.match(helper,/invite.Expires<=Now/);assert.match(helper,/EventAccess.Allowed/);
    const access=read('LummaSimulator.Web/Services/EventAccess.cs');assert.equal([...access.matchAll(/await PresenterService.RevokeEventEnrollment\(/g)].length,2,'Explicit revoke and accepted transfer/recovery use the same cleanup');
    assert.deepEqual(db.prepare('PRAGMA foreign_key_check').all(),[]);assert.equal(db.prepare('PRAGMA integrity_check').get().integrity_check,'ok');
  }finally{db.close();}
});

test('fixed QR matches the owned persisted event rather than silently resuming an open or foreign demo',t=>{
  const db=memory(t);if(!db)return;
  try{
    const source=read('LummaSimulator.Web/Services/EventAccessDemos.cs');
    const sql=source.match(/"""\s*(SELECT COUNT\(\*\) FROM Demos d JOIN PresenterDemoContexts x[\s\S]*?)"""/)[1];
    const own='00000000-0000-0000-0000-000000000401',other='00000000-0000-0000-0000-000000000402';
    db.prepare("INSERT INTO PresenterEventScopes VALUES('experience',?,'Synthetic event',NULL)").run(own);
    db.exec("INSERT INTO Demos(Id,DemoRunId,Name,UserHandle,SessionHash,Route,Created,LastSeen) VALUES('qr-demo','qr-run','Visitor-SYNTHETIC',X'01','own-hash','laptop',1,1)");
    const matches=(kind,id,hash='own-hash')=>db.prepare(sql).get({$hash:hash,$kind:kind,$event:id})['COUNT(*)'];
    assert.equal(matches('experience',own),0);
    db.prepare("INSERT INTO PresenterDemoContexts VALUES('qr-demo','experience',?)").run(own);
    assert.equal(matches('experience',own),1);
    assert.equal(matches('experience',other),0);assert.equal(matches('prize',own),0);assert.equal(matches('experience',own,'other-cookie'),0);
    db.exec("UPDATE Demos SET ClosedAt=1 WHERE Id='qr-demo'");assert.equal(matches('experience',own),0);
  }finally{db.close();}
});

test('production roster SQL preserves the QR alias across refresh and excludes invalid sessions without requiring consent',t=>{
  let DatabaseSync;try{({DatabaseSync}=require('node:sqlite'));}catch{t.skip('Node SQLite unavailable');return;}
  const db=new DatabaseSync(':memory:');
  try{
    db.exec(`CREATE TABLE PrizeEvents(Id,Name,Status,Mode);
      CREATE TABLE PrizeParticipants(Id,EventId,AccountId,State,GroupId,Number);
      CREATE TABLE PrizeRoundParticipants(EventId,ParticipantId,GroupId,Alias);
      CREATE TABLE PrizeGroups(Id,EventId,Number,Status);
      CREATE TABLE Demos(Id,SessionHash,ClosedAt,LastSeen,LastAuthenticatedAt);
      CREATE TABLE PresenterDemoContexts(DemoId,Kind,EventId);
      CREATE TABLE IdentitySessions(Id,UserId,BindingHash,ExpiresAt,RevokedAt,LastActivity);
      INSERT INTO PrizeEvents VALUES('qr-event','Synthetic Test Remco','Open','Group');
      INSERT INTO PrizeGroups VALUES('round-one','qr-event',1,'Open');
      INSERT INTO Demos VALUES('temporary-demo','visitor-binding',NULL,10000000,NULL);
      INSERT INTO PresenterDemoContexts VALUES('temporary-demo','experience','open-demo');`);
    const source=read('LummaSimulator.Web/Services/SessionDemoService.cs');
    const sql=source.match(/"""\s*(SELECT p.Id,p.EventId,d.Id,d.SessionHash,s.Id[\s\S]*?)"""/)[1];
    const statement=db.prepare(sql);statement.setReturnArrays(true);
    const rows=()=>statement.all({$event:'qr-event',$participant:null,$cutoff:8260000,$auth:8200000,$now:10000000});
    assert.equal(rows().length,0,'Registration/open demo alone is not a round participant');
    db.exec(`UPDATE PresenterDemoContexts SET Kind='prize',EventId='qr-event';
      UPDATE Demos SET LastAuthenticatedAt=10000000;
      INSERT INTO IdentitySessions VALUES('identity-session','temporary-demo','visitor-binding',11800000,NULL,10000000);
      INSERT INTO PrizeParticipants VALUES('participant','qr-event','temporary-demo','Active','round-one',1);
      INSERT INTO PrizeRoundParticipants VALUES('qr-event','participant','round-one','Koperotter');`);
    assert.equal(rows()[0][7],'Koperotter');assert.equal(rows()[0][7],rows()[0][7],'Refresh returns the persisted alias');
    assert.equal(rows()[0][0],'participant');
    // No consent table/grant is present. Listing an eligible participant is independent of Start/Open permission.
    for(const update of [
      "UPDATE PresenterDemoContexts SET EventId='another-event'",
      "UPDATE IdentitySessions SET RevokedAt=10000000",
      "UPDATE IdentitySessions SET ExpiresAt=10000000",
      "UPDATE IdentitySessions SET LastActivity=8260000",
      "UPDATE IdentitySessions SET BindingHash='another-binding'",
      "UPDATE Demos SET LastSeen=8260000",
      "UPDATE Demos SET LastAuthenticatedAt=8200000",
      "UPDATE Demos SET LastAuthenticatedAt=10000001",
      "UPDATE Demos SET ClosedAt=10000000",
      "UPDATE PrizeParticipants SET State='Cancelled'",
      "UPDATE PrizeEvents SET Status='Closed'"
    ]){
      db.exec('BEGIN');db.exec(update);assert.equal(rows().length,0,update);db.exec('ROLLBACK');
    }
    assert.equal(rows()[0][7],'Koperotter');
  }finally{db.close();}
});
test('active selector requires the actual owner/member predicate for admins as well',()=>{
 const {DatabaseSync}=require('node:sqlite');const db=new DatabaseSync(':memory:');
 try{
  db.exec("CREATE TABLE PresenterAccounts(Id TEXT,Active INTEGER);CREATE TABLE PresenterKeys(AccountId TEXT);CREATE TABLE PresenterEventScopes(Kind TEXT,SourceId TEXT,OwnerId TEXT);CREATE TABLE PresenterEventMembers(Kind TEXT,EventId TEXT,AccountId TEXT);INSERT INTO PresenterAccounts VALUES('owner',1);INSERT INTO PresenterKeys VALUES('owner');INSERT INTO PresenterEventScopes VALUES('prize','event','owner')");
  const source=read('LummaSimulator.Web/Services/EventAccess.cs');
  const sql=source.match(/var linked=Convert.ToInt64\(await Scalar\(c,tx,"([^"]+)"/)[1];
  const selected=actor=>Object.values(db.prepare(sql).get({$kind:'prize',$id:'event',$actor:actor}))[0]===1;
  assert.equal(selected('owner'),true);assert.equal(selected('admin'),false);
  db.exec("INSERT INTO PresenterEventMembers VALUES('prize','event','admin')");assert.equal(selected('admin'),true);
  db.exec('UPDATE PresenterAccounts SET Active=0');assert.equal(selected('admin'),false);
 }finally{db.close();}
});
test('completed cleaned and active aliases remain candidates; incomplete and withdrawn entries do not',()=>{
 const {DatabaseSync}=require('node:sqlite');const db=new DatabaseSync(':memory:');
 try{
  db.exec("CREATE TABLE PrizeParticipants(Id,EventId,GroupId,State,AccountId);CREATE TABLE PrizeRoundParticipants(EventId,GroupId,ParticipantId,Alias);CREATE TABLE PrizeRoundHistory(EventId,GroupId,ParticipantId,Alias);CREATE TABLE PrizeAudit(EventId,TargetId,Action);CREATE TABLE VisitorExercises(DemoId,CompletedAt);INSERT INTO PrizeParticipants VALUES('p2','e','g','Active','d2'),('p3','e','g','Active','d3');INSERT INTO PrizeRoundParticipants VALUES('e','g','p2','Echovalk'),('e','g','p3','Koperotter');INSERT INTO PrizeRoundHistory VALUES('e','g','p1','Zilvermeeuw');INSERT INTO PrizeAudit VALUES('e','p1','RoundDemoCompleted');INSERT INTO VisitorExercises VALUES('d2',10)");
  const source=read('LummaSimulator.Web/Services/Prizes/PrizeRoundsService.cs');
  const sql=source.match(/"""\s*(SELECT r.ParticipantId,r.Alias,[\s\S]*?ORDER BY r.ParticipantId)\s*"""/)[1];
  const rows=()=>{const q=db.prepare(sql);q.setReturnArrays(true);return q.all({$event:'e',$group:'g'});};
  assert.deepEqual(rows().filter(r=>r[2]&&r[3]).map(r=>r[1]),['Zilvermeeuw','Echovalk']);
  assert.equal(rows().find(r=>r[1]==='Koperotter')[2],0);
  db.exec("UPDATE PrizeParticipants SET State='Cancelled' WHERE Id='p2';INSERT INTO PrizeAudit VALUES('e','p1','RoundEntryWithdrawn')");
  assert.equal(rows().filter(r=>r[2]&&r[3]).length,0,'Cleanup never removes explicit withdrawal');
 }finally{db.close();}
});
test('round polling updates counters and winners without replacing settings forms',async()=>{
 const vm=require('node:vm');const nodes={};let poll;const events={};
 function element(){return {textContent:'',hidden:false,disabled:false,value:'unsaved',open:true,dataset:{url:'/progress',status:'Open'},children:[],replaceChildren(){this.children=[];},append(x){this.children.push(x);},addEventListener(){}};}
 for(const id of ['round-progress','round-occupied','round-completed','round-eligible','round-zero','round-confirm-eligible','round-winners','round-progress-status','close-round','close-round-form'])nodes[id]=element();
 const settings=element(),data={round:{occupied:2,capacity:2,status:'Open',winners:[],winnerCount:0},completed:1,eligible:2};
 vm.runInNewContext(read('LummaSimulator.Web/wwwroot/js/event-round-progress.js'),{document:{hidden:false,getElementById:id=>nodes[id],createElement:element},window:{addEventListener:(name,fn)=>events[name]=fn},AbortController,setTimeout,clearTimeout,setInterval:fn=>poll=fn,fetch:async()=>({ok:true,json:async()=>data})});
 await poll();assert.equal(nodes['round-occupied'].textContent,'2 / 2');assert.equal(nodes['round-completed'].textContent,1);assert.equal(settings.value,'unsaved');assert.equal(settings.open,true);
 data.round.status='Closed';data.round.winners=[{alias:'Zilvermeeuw',prizeName:'Synthetic prize'}];data.round.winnerCount=1;await poll();assert.equal(nodes['close-round'].disabled,true);assert.match(nodes['round-winners'].children[0].textContent,/Zilvermeeuw/);
});
test('participant SQL returns one row per alias, pending registration steps, and retained closed-round history',()=>{
 const {DatabaseSync}=require('node:sqlite');const db=new DatabaseSync(':memory:');
 try{
  db.exec("CREATE TABLE Demos(Id TEXT,Name TEXT,LastAuthenticatedAt INTEGER,ClosedAt INTEGER);CREATE TABLE PresenterDemoContexts(DemoId TEXT,Kind TEXT,EventId TEXT);CREATE TABLE PrizeParticipants(Id TEXT,AccountId TEXT,EventId TEXT,GroupId TEXT,State TEXT);CREATE TABLE PrizeRoundParticipants(EventId TEXT,ParticipantId TEXT,GroupId TEXT,Alias TEXT,IsWinner INTEGER,PrizeName TEXT);CREATE TABLE PrizeRoundHistory(EventId TEXT,ParticipantId TEXT,GroupId TEXT,Alias TEXT,IsWinner INTEGER,PrizeName TEXT);CREATE TABLE VisitorExercises(DemoId TEXT,CompletedAt INTEGER);CREATE TABLE Credentials(DemoId TEXT);CREATE TABLE PrizeAudit(EventId TEXT,TargetId TEXT,Action TEXT)");
  db.exec("INSERT INTO Demos VALUES('d1','Zilvermeeuw',100,NULL),('d2','Echovalk',NULL,NULL),('d3','Koperotter',NULL,NULL);INSERT INTO PresenterDemoContexts VALUES('d1','prize','e'),('d2','prize','e'),('d3','prize','e');INSERT INTO PrizeParticipants VALUES('p1','d1','e','g','Active');INSERT INTO PrizeRoundParticipants VALUES('e','p1','g','Zilvermeeuw',0,NULL);INSERT INTO Credentials VALUES('d1'),('d1'),('d2');INSERT INTO PrizeRoundHistory VALUES('e','p1','g','Zilvermeeuw',0,NULL),('e','p4','g','Maanuil',0,NULL);INSERT INTO PrizeAudit VALUES('e','p4','RoundDemoCompleted')");
  const source=read('LummaSimulator.Web/Services/Prizes/PrizeRoundsService.cs');const sql=source.match(/"""\s*(SELECT COALESCE\(p.Id,d.Id\)[\s\S]*?)"""/)[1];
  const rows=open=>db.prepare(sql).all({$event:'e',$group:'g',$open:open});
  assert.equal(rows(1).length,4,'Multiple passkeys and history overlap do not duplicate the participant');assert.equal(rows(0).length,2,'Closed rounds omit visitors who never joined');
  db.exec("DELETE FROM Demos WHERE Id='d1';DELETE FROM PrizeParticipants WHERE Id='p1';DELETE FROM PrizeRoundParticipants WHERE ParticipantId='p1'");
  assert.equal(rows(0).length,2,'Archive-only participant remains visible after account cleanup');assert.equal(db.prepare(sql).all({$event:'unrelated',$group:'g',$open:1}).length,0);
 }finally{db.close();}
});
test('own participation polling changes draw wording and hides access after expiry',async()=>{
 const vm=require('node:vm');const link={href:'/Participation',hidden:false,dataset:{},textContent:''};let poll;
 const state={status:200,key:'YubiKeyParticipationLink'};
 vm.runInNewContext(read('LummaSimulator.Web/wwwroot/js/participation-status.js'),{document:{hidden:false,getElementById:()=>link},window:{LummaI18n:{t:key=>key},addEventListener(){}},AbortController,setTimeout,clearTimeout,setInterval:fn=>poll=fn,fetch:async()=>({ok:state.status===200,status:state.status,json:async()=>({key:state.key})})});
 await poll();assert.equal(link.textContent,'YubiKeyParticipationLink');state.key='YubiKeyResultLink';await poll();assert.equal(link.textContent,'YubiKeyResultLink');state.status=401;await poll();assert.equal(link.hidden,true);
});

test('saved event prize names stay event-scoped independently of enablement, profiles and stock',t=>{
  const {DatabaseSync}=require('node:sqlite');const db=new DatabaseSync(':memory:');
  try{
    db.exec(`CREATE TABLE PrizeTypes(Id,EventId,Name,Total,Reserved,Issued,Active);
      INSERT INTO PrizeTypes VALUES('off-price','off','Saved disabled prize',5,0,0,1),('on-price','on','Available YubiKey',1,0,0,1),('empty-price','empty','Depleted prize',0,0,0,1);`);
    const source=read('LummaSimulator.Web/Services/Prizes/PrizeRoundsService.cs');
    const nameSql=source.match(/\?"(SELECT Name FROM PrizeTypes WHERE EventId=\$event AND Id=\$fixed)"/)[1];
    const stockSql=source.match(/\?"(SELECT Id,Name,Total,Reserved,Issued FROM PrizeTypes WHERE EventId=\$event AND Id=\$fixed AND Active=1)"/)[1];
    const names=(event,id)=>db.prepare(nameSql).all({$event:event,$fixed:id}).map(r=>r.Name);
    assert.deepEqual(names('off','off-price'),['Saved disabled prize']);
    assert.deepEqual(names('on','on-price'),['Available YubiKey']);
    assert.deepEqual(names('empty','empty-price'),['Depleted prize']);
    assert.deepEqual(names('missing',null),[]);
    assert.deepEqual(names('on','off-price'),[],'Another event never supplies the price name');
    assert.equal(db.prepare(stockSql).get({$event:'empty',$fixed:'empty-price'}).Total,0);
    assert.deepEqual(names('off','off-price'),['Saved disabled prize'],'Switching back keeps the stored event configuration');
    assert.match(source,/names.Count==0\?"RoundNoPrizeConfigured":types.Count==0\?"RoundNoStock"/);
  }finally{db.close();}
});

test('participant polling renders saved winner, loser and excluded alias consistently after refresh',async()=>{
 const vm=require('node:vm'),nodes={};let poll;
 const element=()=>({textContent:'',hidden:false,disabled:false,dataset:{url:'/progress',status:'Open'},children:[],replaceChildren(){this.children=[];},append(x){this.children.push(x);},addEventListener(){}});
 for(const id of ['round-progress','round-occupied','round-completed','round-eligible','round-zero','round-winners','round-participants','round-progress-status'])nodes[id]=element();
 const dict={RoundRowWon:'Gewonnen',RoundRowNoPrize:'Meegeloot, geen prijs gewonnen',RoundRowNotDrawn:'Niet meegeloot',RoundIncompleteAtDraw:'Demo niet afgerond bij de trekking'};
 const data={round:{occupied:3,capacity:3,status:'Closed',winners:[{alias:'Zilvermeeuw',prizeName:'YubiKey demo'}],winnerCount:1},completed:2,eligible:2,participants:[
 {alias:'Zilvermeeuw',registered:true,signedIn:true,completed:true,status:'RoundRowWon',prizeName:'YubiKey demo'},
 {alias:'Echovalk',registered:true,signedIn:true,completed:true,status:'RoundRowNoPrize'},
 {alias:'Koperotter',registered:true,signedIn:true,completed:false,status:'RoundRowNotDrawn',reason:'RoundIncompleteAtDraw'}]};
 vm.runInNewContext(read('LummaSimulator.Web/wwwroot/js/event-round-progress.js'),{document:{hidden:false,getElementById:id=>nodes[id],createElement:element},window:{LummaI18n:{t:key=>dict[key]||key},addEventListener(){}},AbortController,setTimeout,clearTimeout,setInterval:fn=>poll=fn,fetch:async()=>({ok:true,json:async()=>data})});
 await poll();const text=()=>nodes['round-participants'].children.map(row=>row.children.map(c=>c.textContent));const first=text();
 assert.equal(first[0][4],'Gewonnen · YubiKey demo');assert.equal(first[1][4],'Meegeloot, geen prijs gewonnen');assert.equal(first[2][4],'Niet meegeloot · Demo niet afgerond bij de trekking');await poll();assert.deepEqual(text(),first);
 for(const suffix of ['nl-NL','en-GB']){
  const resources=read('LummaSimulator.Web/Resources/Localization/UiText.'+suffix+'.resx');
  assert.match(resources,suffix==='nl-NL'?/Bekijk je demoresultaat →/:/View your demo result →/);
  assert.match(resources,suffix==='nl-NL'?/Bekijk of je een YubiKey hebt gewonnen →/:/See whether you won a YubiKey →/);
 }
});

test('visitor result card uses server messages, a drawn-only orange state and no repeated alias in both languages',async()=>{
 const vm=require('node:vm');for(const lang of ['nl-NL','en-GB']){
 const resources=read('LummaSimulator.Web/Resources/Localization/UiText.'+lang+'.resx');const dict=Object.fromEntries([...resources.matchAll(/<data name="([^"]+)"[^>]*><value>([\s\S]*?)<\/value><\/data>/g)].map(m=>[m[1],m[2]]));
 const nodes={};const node=()=>({hidden:false,textContent:'',href:'/Participation',dataset:{},classes:new Set(),classList:{toggle(key,on){on?this.owner.classes.add(key):this.owner.classes.delete(key);}}});for(const id of ['participation-link','prize-round-status','visitor-round-heading','visitor-round-outcome','visitor-round-prize','visitor-round-claim']){nodes[id]=node();nodes[id].classList.owner=nodes[id];}let poll;const events={};
 let value={key:'YubiKeyResultLink',round:{eventName:'Synthetic event',number:1,alias:'Echovalk',status:'Closed',drawPerformed:true,isWinner:true,prizeName:'YubiKey demo',prizeMessageKey:'VisitorPrizeWon'}};
 vm.runInNewContext(read('LummaSimulator.Web/wwwroot/js/participation-status.js'),{document:{hidden:false,getElementById:id=>nodes[id]},window:{LummaI18n:{t:key=>dict[key]||key},addEventListener:(event,fn)=>events[event]=fn},AbortController,setTimeout,clearTimeout,setInterval:fn=>poll=fn,fetch:async()=>({ok:true,status:200,json:async()=>value})});
 for(const [key,drawn,winner] of [['VisitorPrizeWon',true,true],['VisitorPrizeLost',true,false],['VisitorPrizeNotEntered',true,false],['VisitorNoPrize',false,false],['VisitorPrizesOff',false,false],['VisitorPrizeUnknown',false,false],['VisitorPrizePending',false,false]]){
 value.round.prizeMessageKey=key;value.round.drawPerformed=drawn;value.round.isWinner=winner;await poll();assert.equal(nodes['visitor-round-outcome'].textContent,dict[key]);assert.equal(nodes['prize-round-status'].classes.has('draw-performed'),drawn);assert.equal(nodes['visitor-round-prize'].hidden,!winner);assert.equal(nodes['visitor-round-claim'].hidden,!winner);assert.doesNotMatch(nodes['visitor-round-heading'].textContent,/Echovalk/);
 const before=nodes['visitor-round-outcome'].textContent;await poll();assert.equal(nodes['visitor-round-outcome'].textContent,before);
 }
 }
});

test('closed round makes no progress requests and retains the server-rendered result across page restore',async()=>{
 const vm=require('node:vm');let poll,calls=0;const events={};const root={dataset:{status:'Closed',url:'/Presenter/Events?handler=Progress&id=synthetic&roundId=closed'}},saved={textContent:'Saved winner · Synthetic prize'};
 vm.runInNewContext(read('LummaSimulator.Web/wwwroot/js/event-round-progress.js'),{document:{hidden:false,getElementById:id=>id==='round-progress'?root:id==='round-winners'?saved:null},window:{addEventListener:(name,fn)=>events[name]=fn},AbortController,setTimeout,clearTimeout,setInterval:fn=>poll=fn,fetch:async()=>{calls++;throw new Error('Closed round must not fetch');}});
 await poll();events.pageshow();await poll();assert.equal(calls,0);assert.equal(saved.textContent,'Saved winner · Synthetic prize');
});
