'use strict';
// Source-SQL numbering checks only. C# tests cover concurrent WebAuthn and full initialization.
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const read=file=>fs.readFileSync(path.join(__dirname,'../..',file),'utf8');
test('pre-created demo retains old context; explicitly selected events have independent sequences',t=>{
  let DatabaseSync;try{({DatabaseSync}=require('node:sqlite'));}catch{t.skip('Run the C# ExperienceChecks with Node SQLite unavailable');return;}
  const legacy=read('LummaSimulator.Web/Services/Prizes/PrizeMigration.cs').split('Dictionary<string,string> Legacy = new()')[1].split('\n    };')[0];
  const ddl=read('LummaSimulator.Web/Services/EventAccessMigration.cs').split('internal const string SchemaSql="""')[1].split('"""')[0];
  const demoSource=read('LummaSimulator.Web/Services/DemoService.cs');
  const contextInsert='INSERT INTO PresenterDemoContexts(DemoId,Kind,EventId)'+demoSource.split('INSERT INTO PresenterDemoContexts(DemoId,Kind,EventId)')[1].split('"""')[0];
  const stats=read('LummaSimulator.Web/Services/ExperienceStatistics.cs');
  const literal=pattern=>JSON.parse(stats.match(pattern)[0]);
  const lookup=literal(/"SELECT EventId FROM PresenterDemoContexts WHERE DemoId=\$demo AND Kind='experience'"/);
  const increment=literal(/"UPDATE ExperienceEvents SET Visitors=Visitors\+1 WHERE Id=\$id RETURNING Visitors"/);
  const insertCount=literal(/"INSERT INTO VisitorCounts\(DemoId,EventId,VisitorNumber\) VALUES\(\$demo,\$event,\$number\)"/);
  const bindSource=read('LummaSimulator.Web/Services/EventAccessDemos.cs');
  const bindSql=JSON.parse(bindSource.match(/"INSERT INTO PresenterDemoContexts VALUES\(\$demo,\$kind,\$id\) ON CONFLICT\(DemoId\) DO UPDATE SET Kind=excluded.Kind,EventId=excluded.EventId"/)[0]);
  const old='00000000-0000-0000-0000-000000000201',a='00000000-0000-0000-0000-000000000202',b='00000000-0000-0000-0000-000000000203',unassigned='00000000-0000-0000-0000-000000000004';
  function scenario(selectEvent){
    const db=new DatabaseSync(':memory:');
    try{
      db.exec('PRAGMA foreign_keys=ON');
      for(const m of legacy.matchAll(/\["table:([^"]+)"\]\s*=\s*("(?:\\.|[^"\\])*")/g))db.exec(JSON.parse(m[2]));
      db.exec(ddl);
      const event=(id,status,owner=null)=>{
        db.prepare('INSERT INTO ExperienceEvents(Id,Name,StartedAt,Status) VALUES(?,?,1,?)').run(id,'Synthetic event',status);
        db.prepare("INSERT INTO PresenterEventScopes VALUES('experience',?,'Synthetic event',?)").run(id,owner);
      };
      event(old,'Active');event(unassigned,'Imported');
      db.exec("INSERT INTO PresenterAccounts(Id,Login,Name,Role,Handle,Created,Active,Pending,Activated) VALUES('owner','synthetic-owner','Synthetic','Presenter',X'01',1,1,0,1)");
      function demo(id,selected){
        db.prepare('INSERT INTO Demos(Id,DemoRunId,Name,UserHandle,SessionHash,Route,Created,LastSeen) VALUES(?,?,?,?,?,\'laptop\',1,1)').run(id,id,'Visitor-'+id,Buffer.from(id),id+'-synthetic-binding');
        db.prepare(contextInsert).run({$demo:id,$kind:'experience',$event:unassigned});
        if(selected)db.prepare(bindSql).run({$demo:id,$kind:'experience',$id:selected});
      }
      function number(id){
        db.exec('BEGIN IMMEDIATE');
        try{
          let row=db.prepare('SELECT EventId,VisitorNumber FROM VisitorCounts WHERE DemoId=?').get(id);
          if(!row){const eventId=db.prepare(lookup).get({$demo:id})?.EventId??unassigned;
            const n=db.prepare(increment).get({$id:eventId}).Visitors;
            db.prepare(insertCount).run({$demo:id,$event:eventId,$number:n});row={EventId:eventId,VisitorNumber:n};}
          db.exec('COMMIT');return {...row};
        }catch(error){db.exec('ROLLBACK');throw error;}
      }
      for(let i=1;i<=6;i++){demo('old-'+i,old);assert.equal(number('old-'+i).VisitorNumber,i);}
      demo('next',old); // An explicitly selected, pre-existing context never moves on a switch.
      db.prepare("UPDATE ExperienceEvents SET Status='Closed' WHERE Id=?").run(old);event(a,'Active','owner');
      if(selectEvent)db.prepare(bindSql).run({$demo:'next',$kind:'experience',$id:a});
      const receipt=number('next');
      if(!selectEvent){
        assert.deepEqual(receipt,{EventId:old,VisitorNumber:7});
        assert.equal(db.prepare('SELECT Visitors FROM ExperienceEvents WHERE Id=?').get(a).Visitors,0);
        t.diagnostic(`Original fixture: expected=1/event=${a}; actual=7/event=${old}; old counter=7; new counter=0`);
      }else{
        assert.deepEqual(receipt,{EventId:a,VisitorNumber:1});event(b,'Active','owner');
        const sequences={a:[1],b:[]};
        for(let i=1;i<=4;i++)for(const [key,id] of [['a',a],['b',b]]){
          if(key==='a'&&i===1)continue;demo(key+i,id);sequences[key].push(number(key+i).VisitorNumber);
        }
        assert.deepEqual(sequences,{a:[1,2,3,4],b:[1,2,3,4]});
        db.exec(ddl);db.exec(ddl); // Additive schema reinitialization, not the C# initializer.
        for(const [key,id] of [['a',a],['b',b]]){demo(key+'5',id);assert.deepEqual(number(key+'5'),{EventId:id,VisitorNumber:5});}
        assert.deepEqual(number('next'),{EventId:a,VisitorNumber:1});
        assert.deepEqual(db.prepare('PRAGMA foreign_key_check').all(),[]);
        assert.equal(db.prepare('PRAGMA integrity_check').get().integrity_check,'ok');
      }
    }finally{db.close();}
  }
  scenario(false);scenario(true);
});
