import { connectDirectoryFilterMenu } from './PropFlowFilterPopover.razor.js?v=20261125.5';
export function open(root, dotnet) {
    const trigger=root.querySelector('.pf-select__trigger'), menu=root.querySelector('[role=listbox]');
    const events=new AbortController(); let closing=false,frame;
    menu.showPopover();
    const directory=connectDirectoryFilterMenu(trigger,menu,392,close);
    function position(){
        if(directory){directory.position();return;}
        const r=trigger.getBoundingClientRect(),v=window.visualViewport;
        const x=v?.offsetLeft??0,y=v?.offsetTop??0,w=v?.width??innerWidth,h=v?.height??innerHeight;
        const below=y+h-r.bottom-14,above=r.top-y-14,up=below<220&&above>below;
        const max=Math.max(60,Math.min(336,up?above:below));
        menu.style.width=Math.min(r.width,w-16)+'px';menu.style.maxHeight=max+'px';
        menu.style.left=Math.max(x+8,Math.min(r.left,x+w-menu.offsetWidth-8))+'px';
        menu.style.top=Math.max(y+8,Math.min(up?r.top-menu.offsetHeight-6:r.bottom+6,y+h-menu.offsetHeight-8))+'px';
    }
    const options={signal:events.signal};
    function disconnect(){closing=true;events.abort();cancelAnimationFrame(frame);directory?.disconnect();if(menu.matches(':popover-open'))menu.hidePopover();}
    function close(){disconnect();trigger.focus({preventScroll:true});void dotnet.invokeMethodAsync('CloseFromOutside');}
    menu.addEventListener('beforetoggle',e=>{if(e.newState==='closed'&&!closing)e.preventDefault();},options);
    // Close after the click target is established. Collapsing the expanded
    // drawer on pointerdown can move its Apply button before pointerup.
    document.addEventListener(directory?'click':'pointerdown',e=>{if(!root.contains(e.target)){disconnect();void dotnet.invokeMethodAsync('CloseFromOutside');}},{...options,capture:true});
    root.addEventListener('keydown',e=>{
        if(e.key==='Escape'){e.preventDefault();e.stopPropagation();close();return;}
        if(!['ArrowDown','ArrowUp','Home','End'].includes(e.key)||e.target.matches('input')&&!['ArrowDown','ArrowUp'].includes(e.key))return;
        e.preventDefault();e.stopPropagation();
        const items=[...menu.querySelectorAll('[role=option]:not(:disabled)')];if(!items.length)return;
        const i=items.indexOf(document.activeElement),next=e.key==='Home'?0:e.key==='End'?items.length-1:e.key==='ArrowDown'?(i+1)%items.length:(i<=0?items.length:i)-1;
        items[next].focus({preventScroll:true});items[next].scrollIntoView({block:'nearest'});
    },options);
    function reposition(e){if(e?.target&&menu.contains(e.target))return;cancelAnimationFrame(frame);frame=requestAnimationFrame(position);}
    window.addEventListener('resize',reposition,options);window.addEventListener('scroll',reposition,{...options,capture:true});
    window.visualViewport?.addEventListener('resize',reposition,options);window.visualViewport?.addEventListener('scroll',reposition,options);
    position();return {disconnect};
}
