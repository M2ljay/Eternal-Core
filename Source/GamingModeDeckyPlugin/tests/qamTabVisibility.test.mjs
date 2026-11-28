import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import vm from 'node:vm';
const api={};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL('../src/qamTabVisibility.ts',import.meta.url),'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports:api,require:()=>({})});
function node(id,value='',priority='',label=''){
 let current=value,rank=priority,writes=0;
 return {id,isConnected:true,getAttribute:name=>name==='aria-label'&&label?label:null,get writes(){return writes;},setLabel(v){label=v;},
  style:{getPropertyValue:()=>current,getPropertyPriority:()=>rank,setProperty:(_k,v,p)=>{current=v;rank=p;writes++;},removeProperty:()=>{current='';rank='';writes++;}}};
}
test('only hidden Steam tabs are written; shown tabs, Decky, content and protected ids are never touched',()=>{
 const nodes=new Map(['0','3','4','5','6','7','5261568','999','100'].map(id=>[`quickaccess_tab_${id}`,node(`quickaccess_tab_${id}`)]));
 const content=node('quickaccess_content_6'),header=node('header');nodes.set(content.id,content);nodes.set(header.id,header);
 const projection=api.createQamTabVisibility(()=>[{getElementById:id=>nodes.get(id)}],{protectedIds:()=>['100','5261568']});
 const ids=['0','3','4','5','6','7'];
 for(const hidden of ids){projection.update(new Map(ids.map(id=>[id,id!==hidden])));for(const id of ids){assert.equal(nodes.get(`quickaccess_tab_${id}`).style.getPropertyValue('display'),id===hidden?'none':'');}}
 // Decky (999), Core (100) and a plugin shortcut (5261568) can never be hidden, even when asked.
 projection.update(new Map([['999',false],['100',false],['5261568',false]]));
 for(const id of ['999','100','5261568'])assert.equal(nodes.get(`quickaccess_tab_${id}`).style.getPropertyValue('display'),'');
 assert.equal(content.style.getPropertyValue('display'),'');assert.equal(header.style.getPropertyValue('display'),'');
 projection.stop();for(const n of nodes.values())assert.equal(n.style.getPropertyValue('display'),'');
});
test('a numeric id Steam re-assigns to another tab is not hidden, and the same answer is never re-written',()=>{
 const tab=node('quickaccess_tab_5','','', 'Notifications');const nodes=new Map([[tab.id,tab]]);
 const projection=api.createQamTabVisibility(()=>[{getElementById:id=>nodes.get(id)}]);
 projection.update(new Map([['5',false]]));
 assert.equal(tab.style.getPropertyValue('display'),'none');
 const writes=tab.writes;
 for(let i=0;i<20;i++){projection.update(new Map([['5',false]]));projection.reconcile();}
 assert.equal(tab.writes,writes,'no write when nothing changed');
 // Plugins re-register: React replaces the node and id 5 now belongs to another tab.
 const other=node('quickaccess_tab_5','','','Awesome Mix');nodes.set(other.id,other);tab.isConnected=false;
 projection.reconcile();
 assert.equal(other.style.getPropertyValue('display'),'','the re-assigned id keeps its tab visible');
 projection.stop();
});
test('mutations are coalesced into one debounced pass and style changes never trigger one',()=>{
 const tab=node('quickaccess_tab_4');const nodes=new Map([[tab.id,tab]]);
 let callback,timers=[];
 class Observer{constructor(cb){callback=cb;}observe(){}disconnect(){}}
 const doc={getElementById:id=>nodes.get(id),defaultView:{MutationObserver:Observer},documentElement:{}};
 const projection=api.createQamTabVisibility(()=>[doc],{schedule:(run,ms)=>{timers.push({run,ms});return timers.length;},cancel:()=>{}});
 projection.update(new Map([['4',false]]));
 assert.equal(tab.style.getPropertyValue('display'),'none');
 // React re-renders the rail: a fresh node, then a storm of mutations.
 const fresh=node('quickaccess_tab_4');nodes.set(fresh.id,fresh);tab.isConnected=false;
 for(let i=0;i<50;i++)callback([{addedNodes:[{}],removedNodes:[]}]);
 assert.equal(timers.length,1,'one pass scheduled for the whole storm');
 assert.equal(timers[0].ms,api.QAM_VISIBILITY_DEBOUNCE_MS);
 callback([{type:'attributes',attributeName:'style',addedNodes:[],removedNodes:[]}]);
 assert.equal(timers.length,1,'a style change schedules nothing');
 timers[0].run();
 assert.equal(fresh.style.getPropertyValue('display'),'none','the fresh node is hidden once');
 projection.stop();
});
test('cleanup restores original inline style and preserves subsequent foreign writes',()=>{
 const one=node('quickaccess_tab_4','grid','important'),two=node('quickaccess_tab_6');const nodes=new Map([[one.id,one],[two.id,two]]);
 const projection=api.createQamTabVisibility(()=>[{getElementById:id=>nodes.get(id)}]);projection.update(new Map([['4',false],['6',false]]));
 two.style.setProperty('display','block','important');projection.stop();assert.equal(one.style.getPropertyValue('display'),'grid');assert.equal(one.style.getPropertyPriority('display'),'important');assert.equal(two.style.getPropertyValue('display'),'block');
});
