import { shapeNames, squareKinds, pathKinds, drawnGeometry, shapePath, worldPoint, boundedPointFrame, extendDivider, normalizeAngle, frameInside, selectInRectangle, nextSpaceLabel, pasteShapes, selectionGaps, arrangeShapes, measurementScale, shapeBounds, groupFrame, rotatedShapes, smartPlacement } from './parking-shapes.js?v=20261125-smart-guides-1';
const NS = 'http://www.w3.org/2000/svg';
export const kinds = Object.keys(shapeNames);
export const copyDrawing = drawing => structuredClone(drawing);
export function freshShape(kind, label, x, y, width, height) {
  return { id: crypto.randomUUID(), kind, label, x, y, width, height, rotation: 0 };
}
export { nextSpaceLabel };
export function sourceUrl(bytes, mime) { return URL.createObjectURL(new Blob([bytes], { type: mime })); }
export function releaseUrl(url) { if (url?.startsWith('blob:')) URL.revokeObjectURL(url); }

export function create(root, dotnet, initial, initialSpaces, editing) {
  let drawing = copyDrawing(initial), spaces = initialSpaces || [], editable = editing, tool = 'select', selected = null;
  let disposed = false, dragging, preview, marquee, view, snap = !initial.digitization, referenceImage = null, scaleStart = null, selection = new Set();
  let clipboard = [], pasteCount = 0;
  let measureAxis = 'x', measureUnit = 'm';
  let smartGuides=true,liveGuides=null;
  const undo = [], redo = [], listeners = [], hatchId = 'parking-hatch-' + crypto.randomUUID();
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('aria-label', editing ? 'Trình chỉnh sửa sơ đồ bãi đỗ' : 'Sơ đồ bãi đỗ đang quản lý');
  svg.setAttribute('tabindex', '0'); svg.setAttribute('role', 'group'); root.replaceChildren(svg);
  const on = (target, event, fn, options) => { target.addEventListener(event, fn, options); listeners.push(() => target.removeEventListener(event, fn, options)); };
  const notify = (name, ...args) => { if (!disposed) void dotnet.invokeMethodAsync(name, ...args).catch(() => {}); };
  const element = (name, attrs = {}) => { const e = document.createElementNS(NS, name); for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v); return e; };
  function fit() {
    const bounds=drawing.infiniteCanvas?drawing.shapes.filter(s=>s.kind!=='TRACE').map(shapeBounds):[];
    if(bounds.length){const x=Math.min(...bounds.map(b=>b.left)),y=Math.min(...bounds.map(b=>b.top)),w=Math.max(100,Math.max(...bounds.map(b=>b.right))-x),h=Math.max(100,Math.max(...bounds.map(b=>b.bottom))-y),pad=Math.max(w,h)*.08;view={x:x-pad,y:y-pad,w:w+2*pad,h:h+2*pad};}
    else view={x:-30,y:-30,w:drawing.width+60,h:drawing.height+60};viewport();
  }
  function displayScale() { return Math.min(svg.clientWidth / view.w, svg.clientHeight / view.h) || 1; }
  function viewport() { svg.setAttribute('viewBox', `${view.x} ${view.y} ${view.w} ${view.h}`);if(drawing.infiniteCanvas)for(const sheet of svg.querySelectorAll('.parking-sheet,.parking-grid-sheet'))for(const [key,value]of Object.entries({x:view.x-view.w,y:view.y-view.h,width:view.w*3,height:view.h*3}))sheet.setAttribute(key,value); svg.querySelectorAll('.parking-resize').forEach(handle => handle.setAttribute('r', 5 / displayScale())); for (const group of svg.querySelectorAll('[data-id]')) { const shape = drawing.shapes.find(s => s.id === group.dataset.id); if (shape) geometry(group, shape); } updateLabels(); renderRulers(); renderGroupControls(); renderLiveGuides(); }
  function updateLabels() {
    const scale = displayScale();
    const hatch = svg.querySelector('#' + hatchId);
    if (hatch) { hatch.setAttribute('width', 8 / scale); hatch.setAttribute('height', 8 / scale); hatch.firstElementChild.setAttribute('d', `M0 0V${8 / scale}M${4 / scale} 0V${8 / scale}`); }
    for (const label of svg.querySelectorAll('.parking-shape text')) {
      const base = Number(label.dataset.baseSize), chosen = label.parentElement.dataset.id === selected;
      label.setAttribute('font-size', chosen ? Math.max(base, 10 / scale) : base);
      label.style.visibility = chosen || base * scale >= 8 ? 'visible' : 'hidden';
    }
  }
  const chosenShapes = () => drawing.shapes.filter(s=>selection.has(s.id)&&s.kind!=='TRACE');
  const formatMeasure = value => new Intl.NumberFormat('vi-VN',{maximumFractionDigits:2}).format(Math.abs(value)<1e-8?0:value);
  function spacingSummary() {
    const gaps=selectionGaps(chosenShapes(),measureAxis);if(!gaps.length)return '';
    const {factor,unit}=measurementScale(drawing,measureUnit),values=gaps.map(g=>g.gap),equal=values.every(v=>Math.abs(v-values[0])<1e-6);
    if(!factor)return 'Đặt kích thước thực của một hình hoặc hiệu chuẩn bản gốc để đo bằng m/cm.';
    return `${measureAxis==='x'?'Ngang':'Dọc'}: ${values.slice(0,8).map(v=>formatMeasure(v*factor)).join(' · ')}${values.length>8?' …':''} ${unit}. ${values.some(v=>v< -1e-6)?'Khe âm: khung bao chồng theo trục đo.':values.length>1?(equal?'Các khe hở bằng nhau.':'Các khe hở chưa đều.'):'Khe hở giữa hai hình.'}`;
  }
  function renderRulers() {
    svg.querySelector('.parking-rulers')?.remove();if(!editable)return;
    const gaps=selectionGaps(chosenShapes(),measureAxis);if(!gaps.length)return;
    const layer=element('g',{class:'parking-rulers','pointer-events':'none','aria-hidden':'true'}),scale=displayScale(),{factor,unit}=measurementScale(drawing,measureUnit);if(!factor)return;
    for(const {from,to,gap} of gaps) {
      const a=from.bounds,b=to.bounds,horizontal=measureAxis==='x';
      const edge=Math.max(horizontal?a.bottom:a.right,horizontal?b.bottom:b.right),limit=horizontal?drawing.height:drawing.width;
      const line=drawing.infiniteCanvas||edge+26/scale<limit?edge+20/scale:Math.max(12/scale,Math.min(horizontal?a.top:a.left,horizontal?b.top:b.left)-20/scale);
      const start=horizontal?a.right:a.bottom,end=horizontal?b.left:b.top,mid=(start+end)/2,text=`${formatMeasure(gap*factor)} ${unit}`;
      const group=element('g',{class:gap< -1e-6?'parking-gap is-overlap':'parking-gap','data-gap':gap});
      group.append(element('path',{d:horizontal?`M${start} ${line}H${end}M${start} ${line-5/scale}V${line+5/scale}M${end} ${line-5/scale}V${line+5/scale}`:`M${line} ${start}V${end}M${line-5/scale} ${start}H${line+5/scale}M${line-5/scale} ${end}H${line+5/scale}`}));
      const x=horizontal?mid:line+8/scale,y=horizontal?line-9/scale:mid;
      group.append(element('rect',{x:x-(horizontal?text.length*3.5:2)/scale,y:y-9/scale,width:(text.length*7+4)/scale,height:17/scale,rx:3/scale}));
      const label=element('text',{x,y,'font-size':12/scale,'dominant-baseline':'middle','text-anchor':horizontal?'middle':'start'});label.textContent=text;group.append(label);layer.append(group);
    }
    svg.append(layer);
  }
  function notifySelection() { notify('OnParkingSelected', selected); notify('OnParkingSelectionChanged', [...selection]); notify('OnParkingSpacingChanged',spacingSummary()); }
  function clearGuides(){liveGuides=null;svg.querySelector('.parking-smart-guides')?.remove();}
  function renderLiveGuides(){
    svg.querySelector('.parking-smart-guides')?.remove();if(!liveGuides)return;
    const layer=element('g',{class:'parking-smart-guides','pointer-events':'none','aria-hidden':'true'}),scale=displayScale();
    for(const g of liveGuides.guides){const pad=12/scale;layer.append(element('line',{class:'parking-alignment-guide','data-axis':g.axis,'data-position':g.position,x1:g.axis==='x'?g.position:g.from-pad,y1:g.axis==='x'?g.from-pad:g.position,x2:g.axis==='x'?g.position:g.to+pad,y2:g.axis==='x'?g.to+pad:g.position}));}
    const {factor,unit}=measurementScale(drawing,measureUnit);
    if(factor)for(const d of liveGuides.distances){if(d.gap< -1e-6)continue;const horizontal=d.axis==='x',cross=d.cross+22/scale,mid=(d.start+d.end)/2,text=`${formatMeasure(d.gap*factor)} ${unit}${d.equal?' =':''}`,group=element('g',{class:'parking-smart-distance'+(d.equal?' is-equal':''),'data-gap':d.gap});
      group.append(element('path',{d:horizontal?`M${d.start} ${cross}H${d.end}M${d.start} ${cross-4/scale}V${cross+4/scale}M${d.end} ${cross-4/scale}V${cross+4/scale}`:`M${cross} ${d.start}V${d.end}M${cross-4/scale} ${d.start}H${cross+4/scale}M${cross-4/scale} ${d.end}H${cross+4/scale}`}));
      const x=horizontal?mid:cross+7/scale,y=horizontal?cross-9/scale:mid;group.append(element('rect',{x:x-(horizontal?text.length*3.5:2)/scale,y:y-9/scale,width:(text.length*7+4)/scale,height:17/scale,rx:3/scale}));const label=element('text',{x,y,'font-size':12/scale,'dominant-baseline':'middle','text-anchor':horizontal?'middle':'start'});label.textContent=text;group.append(label);layer.append(group);
    }svg.append(layer);
  }
  function guidePlacement(shapes,event,fallbackX=0,fallbackY=0){
    if(!smartGuides||event.altKey){clearGuides();return shapes.map(s=>({...structuredClone(s),x:s.x+(event.altKey?0:fallbackX),y:s.y+(event.altKey?0:fallbackY)}));}
    const result=smartPlacement(shapes,drawing.shapes,displayScale(),{fallbackX,fallbackY});liveGuides=result;return result.shapes;
  }
  function renderGroupControls() {
    svg.querySelector('.parking-group-selection')?.remove();if(!editable||selection.size<2)return;
    const rotating=dragging?.mode==='rotate-group',frame=rotating?dragging.frame:groupFrame(chosenShapes());if(!frame)return;
    const scale=displayScale(),x=frame.cx,y=frame.y-28/scale,layer=element('g',{class:'parking-group-selection',transform:`rotate(${rotating?dragging.angle:0} ${frame.cx} ${frame.cy})`});
    layer.append(element('rect',{class:'parking-group-frame',x:frame.x,y:frame.y,width:frame.width,height:frame.height}),element('path',{class:'parking-group-frame',d:`M${x} ${frame.y}V${y}`}));
    layer.append(element('circle',{class:'parking-group-pivot',cx:frame.cx,cy:frame.cy,r:3/scale}));
    const handle=element('circle',{class:'parking-resize parking-group-rotate','data-group-rotate':'true',cx:x,cy:y,r:6/scale,tabindex:0,role:'button','aria-label':'Xoay cả nhóm. Phím trái/phải xoay 1 độ, giữ Shift để xoay 15 độ.'});
    const title=element('title');title.textContent='Kéo để xoay cả nhóm; giữ Shift để xoay theo bước 15°';handle.append(title);layer.append(handle);svg.append(layer);
  }
  function selectMany(ids) { const visible = new Set(drawing.shapes.filter(s => s.kind !== 'TRACE').map(s => s.id)); selection = new Set(ids.filter(id => visible.has(id))); selected = selection.size === 1 ? [...selection][0] : null; render(); notifySelection(); }
  function commit(previous) { if(drawing.recognitionReview)drawing.recognitionReview.stale=true;undo.push(previous); while (undo.length > 1 && (undo.length > 30 || undo.reduce((sum, x) => sum + JSON.stringify(x).length, 0) > 8000000)) undo.shift(); redo.length = 0; tool = 'select'; if (selected) selection = new Set([selected]); render(); notify('OnParkingChanged', drawing, selected); notifySelection(); }
  function select(id) { selectMany(id ? [id] : []); }
  function transform(shape) { return `rotate(${shape.rotation || 0} ${shape.x + shape.width / 2} ${shape.y + shape.height / 2})`; }
  function geometry(group, shape) {
    group.setAttribute('transform', transform(shape));
    const rect = group.querySelector('.parking-body:is(rect)'), polygon = group.querySelector('.parking-body:is(polygon)'), ellipse = group.querySelector('.parking-body:is(ellipse)');
    if (rect) for (const [key, value] of Object.entries({ x: shape.x, y: shape.y, width: shape.width, height: shape.height })) rect.setAttribute(key, value);
    if (polygon) polygon.setAttribute('points', shape.outline.map(p => `${shape.x + p.x * shape.width},${shape.y + p.y * shape.height}`).join(' '));
    if (ellipse) for (const [key, value] of Object.entries({ cx: shape.x + shape.width / 2, cy: shape.y + shape.height / 2, rx: shape.width / 2, ry: shape.height / 2 })) ellipse.setAttribute(key, value);
    for (const path of group.querySelectorAll('path.parking-body,path.parking-hit')) path.setAttribute('d', shapePath(shape));
    const text = group.querySelector('text'); if (text) { const cx=shape.x+shape.width/2,cy=shape.y+shape.height/2,angle=((shape.rotation||0)%360+360)%360;text.setAttribute('x',cx);text.setAttribute('y',cy);text.setAttribute('transform',angle>90&&angle<=270?`rotate(180 ${cx} ${cy})`:'');text.textContent=shape.label; }
    for (const handle of group.querySelectorAll('.parking-resize')) {
      const p = handle.dataset.rotate ? { x: .5, y: -24 / displayScale() / shape.height } : handle.dataset.tail ? { x: .5, y: 1 } : handle.dataset.control !== undefined ? shape.controlPoints[Number(handle.dataset.control)] : handle.dataset.vertex === undefined ? { x: 1, y: 1 } : shape.outline[Number(handle.dataset.vertex)];
      handle.setAttribute('cx', shape.x + p.x * shape.width); handle.setAttribute('cy', shape.y + p.y * shape.height);
    }
  }
  function render() {
    svg.classList.toggle('parking-editing', editable);
    svg.classList.toggle('parking-infinite', !!drawing.infiniteCanvas);
    svg.classList.toggle('parking-comparing', editable && !!referenceImage);

    const defs = element('defs'), pattern = element('pattern', { id: 'parking-grid', width: 20, height: 20, patternUnits: 'userSpaceOnUse' });
    pattern.append(element('path', { d: 'M20 0H0V20', class: 'parking-grid-line', fill: 'none' })); defs.append(pattern);
    const hatch = element('pattern', { id: hatchId, width: 8, height: 8, patternUnits: 'userSpaceOnUse', patternTransform: 'rotate(45)' });
    hatch.append(element('path', { d: 'M0 0V8M4 0V8', class: 'parking-hatch-line' })); defs.append(hatch);
    svg.style.setProperty('--parking-hatch', `url(#${hatchId})`);
    svg.replaceChildren(defs, element('rect', { x: 0, y: 0, width: drawing.width, height: drawing.height, class: 'parking-sheet' }));
    if (editable && referenceImage) svg.append(element('image', { href: referenceImage, x: 0, y: 0, width: drawing.width, height: drawing.height, preserveAspectRatio: 'none', opacity: .45, 'pointer-events': 'none', 'aria-label': 'Bản gốc để đối chiếu' }));
    if (editable && snap) svg.append(element('rect', { class:'parking-grid-sheet',x: 0, y: 0, width: drawing.width, height: drawing.height, fill: 'url(#parking-grid)', 'pointer-events': 'none' }));
    const status = new Map(spaces.map(x => [x.id, x.status]));
    const sourceIssues=new Set((drawing.recognitionReview?.stale?[]:drawing.recognitionReview?.issues||[]).flatMap(issue=>issue.shapeIds||[]));
    const layers = ['LANE', 'WALL', 'WALL_SQUARE', 'WALL_CIRCLE', 'RECT', 'SQUARE', 'SPACE', 'LINE', 'CURVE', 'DIVIDER', 'GATE', 'ENTRANCE', 'EXIT', 'LABEL'];
    const sorted = [...drawing.shapes].sort((a, b) => layers.indexOf(a.kind) - layers.indexOf(b.kind));
    for (const shape of sorted) {
      if (shape.kind === 'TRACE') continue; // Legacy contour data is never the editable object diagram.
      const group = element('g', { 'data-id': shape.id, class: `parking-shape kind-${shape.kind.toLowerCase()} state-${(status.get(shape.id) || 'AVAILABLE').toLowerCase()}${shape.needsReview ? ' needs-review' : ''}${sourceIssues.has(shape.id)?' has-source-issue':''}${selection.has(shape.id) ? ' is-selected' : ''}` });
      if (pathKinds.has(shape.kind)) group.append(element('path', { class: 'parking-hit', fill: 'none', stroke: 'transparent', 'stroke-width': 14, 'vector-effect': 'non-scaling-stroke', 'pointer-events': 'stroke' }));
      group.append(element(pathKinds.has(shape.kind) ? 'path' : shape.kind === 'WALL_CIRCLE' ? 'ellipse' : shape.outline?.length === 4 ? 'polygon' : 'rect', { class: 'parking-body', 'fill-rule': 'evenodd' }));
      const fontSize = Math.max(9, Math.min(drawing.width / 70, shape.height * .55, shape.width / Math.max(1, shape.label.length) * 1.3));
      const text = element('text', { 'text-anchor': 'middle', 'dominant-baseline': 'middle', 'font-size': fontSize, 'data-base-size': fontSize }); group.append(text);
      const title = element('title'); title.textContent = shape.label || shapeNames[shape.kind] || 'Đối tượng sơ đồ'; group.append(title);
      if (editable && selected === shape.id) {
        const radius = 5 / displayScale();
        if (shape.controlPoints) shape.controlPoints.forEach((_, index) => group.append(element('circle', { r: radius, 'data-control': index, class: 'parking-resize parking-control' })));
        else if (shape.outline?.length === 4) shape.outline.forEach((_, index) => group.append(element('circle', { r: radius, 'data-vertex': index, class: 'parking-resize' })));
        else group.append(element('circle', { r: radius, 'data-resize': 'true', class: 'parking-resize' }));
        if (shape.kind === 'DIVIDER') group.append(element('circle', { r: radius, 'data-tail': 'true', class: 'parking-resize parking-tail' }));
        group.append(element('circle', { r: radius, 'data-rotate': 'true', class: 'parking-resize parking-rotate' }));
      }
      if (editable || shape.kind === 'SPACE') { group.setAttribute('tabindex', '0'); group.setAttribute('role', 'button'); group.setAttribute('aria-label', (shape.label || 'Đối tượng sơ đồ') + ': ' + (shape.needsReview ? 'Cần đối chiếu' : ({ AVAILABLE: 'Trống', IN_USE: 'Đang sử dụng', MAINTENANCE: 'Bảo trì' }[status.get(shape.id) || 'AVAILABLE']))); }
      geometry(group, shape); svg.append(group);
    }
    if (editable && drawing.scale) {
      const s = drawing.scale; svg.append(element('line', { x1: s.start.x, y1: s.start.y, x2: s.end.x, y2: s.end.y, class: 'parking-scale-line', 'pointer-events': 'none' }));
    }
    svg.style.cursor = tool === 'pan' ? 'grab' : tool === 'select' ? 'default' : 'crosshair';
    viewport();
  }
  function point(event, matrix) { return new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix || svg.getScreenCTM().inverse()); }
  const bounded = p => drawing.infiniteCanvas?{x:p.x,y:p.y}:{x:Math.max(0,Math.min(drawing.width,p.x)),y:Math.max(0,Math.min(drawing.height,p.y))};
  const align = value => snap ? Math.round(value / 10) * 10 : Math.round(value);
  function down(event) {
    if (event.button !== 0 && event.button !== 1) return;
    clearGuides();
    svg.focus({ preventScroll: true }); const matrix = svg.getScreenCTM().inverse(), p = point(event, matrix);
    if(editable&&event.button===0&&event.target.closest('[data-group-rotate]')) {
      const originals=chosenShapes().map(s=>structuredClone(s)),frame=groupFrame(originals);if(!frame||originals.length<2)return;
      dragging={mode:'rotate-group',p,matrix,before:copyDrawing(drawing),originals,frame,pivot:{x:frame.cx,y:frame.cy},startAngle:Math.atan2(p.y-frame.cy,p.x-frame.cx),angle:0};
      event.preventDefault();svg.setPointerCapture(event.pointerId);return;
    }
    if (event.button === 1 || tool === 'pan') { dragging = { mode: 'pan', p, matrix, view: { ...view } }; }
    else if (editable && tool === 'calibrate') {
      if (!scaleStart) { scaleStart = bounded(p); return; }
      const end = bounded(p);
      if (Math.hypot(end.x - scaleStart.x, end.y - scaleStart.y) < 4) return;
      const before = copyDrawing(drawing); drawing.scale = { start: scaleStart, end, length: drawing.scale?.length || 0, unit: drawing.scale?.unit || '', confirmed: false }; scaleStart = null; tool = 'select'; commit(before); return;
    }
    else if (!editable) { const item = event.target.closest('[data-id]'); const shape = drawing.shapes.find(x => x.id === item?.dataset.id); if (shape?.kind === 'SPACE') select(shape.id); return; }
    else if (tool === 'region' && !event.target.closest('.parking-resize')) { const previousSelection = [...selection]; selection.clear(); selected = null; render(); dragging = { mode: 'region', p, matrix, previousSelection }; }
    else if (tool !== 'select' && !event.target.closest('.parking-resize')) { if (drawing.shapes.filter(s => s.kind !== 'TRACE').length >= 1000) return; const start=bounded(p);dragging = { mode: 'draw', p: { x: align(start.x), y: align(start.y) } }; }
    else {
      const group = event.target.closest('[data-id]');
      if(event.shiftKey&&group&&!event.target.closest('.parking-resize')) {
        const ids=new Set(selection),id=group.dataset.id;if(ids.has(id))ids.delete(id);else ids.add(id);
        selectMany([...ids]);svg.focus({preventScroll:true});event.preventDefault();return;
      }
      if (selection.size > 1 && selection.has(group?.dataset.id) && !event.target.closest('.parking-resize')) {
        dragging = { mode: 'move-group', p, matrix, before: copyDrawing(drawing), originals: drawing.shapes.filter(s=>selection.has(s.id)).map(s=>structuredClone(s)) };
        event.preventDefault(); svg.setPointerCapture(event.pointerId); return;
      }
      select(group?.dataset.id || null);
      const shape = drawing.shapes.find(x => x.id === selected); if (!shape) return;
      dragging = { mode: event.target.dataset.rotate ? 'rotate' : event.target.dataset.tail ? 'tail' : event.target.dataset.control !== undefined ? 'control' : event.target.dataset.vertex !== undefined ? 'vertex' : event.target.dataset.resize ? 'resize' : 'move', vertex: Number(event.target.dataset.vertex), control: Number(event.target.dataset.control), p, before: copyDrawing(drawing), original: structuredClone(shape), group: svg.querySelector(`[data-id="${shape.id}"]`) };
    }
    event.preventDefault(); svg.setPointerCapture(event.pointerId);
  }
  function move(event) {
    if (!dragging) return; const p = point(event, dragging.matrix), d = dragging;
    if (d.mode === 'pan') { view.x = d.view.x - (p.x - d.p.x); view.y = d.view.y - (p.y - d.p.y); viewport(); return; }
    if(d.mode==='rotate-group') {
      if(Math.hypot(p.x-d.pivot.x,p.y-d.pivot.y)*displayScale()<4)return;
      let angle=Math.round((Math.atan2(p.y-d.pivot.y,p.x-d.pivot.x)-d.startAngle)*180/Math.PI*10)/10;if(event.shiftKey)angle=Math.round(angle/15)*15;
      const result=rotatedShapes(d.originals,drawing.width,drawing.height,angle,drawing.infiniteCanvas,d.pivot);
      if(result.shapes){d.angle=angle;for(const shape of result.shapes){Object.assign(drawing.shapes.find(s=>s.id===shape.id),shape);geometry(svg.querySelector(`[data-id="${shape.id}"]`),shape);}renderRulers();renderGroupControls();}return;
    }
    if (d.mode === 'region') {
      const end = bounded(p);
      if (!marquee) { marquee = element('rect', { class: 'parking-selection-box', 'pointer-events': 'none' }); svg.append(marquee); }
      for (const [key, value] of Object.entries({ x: Math.min(d.p.x, end.x), y: Math.min(d.p.y, end.y), width: Math.abs(end.x - d.p.x), height: Math.abs(end.y - d.p.y) })) marquee.setAttribute(key, value);
      selection = new Set(Math.abs(end.x - d.p.x) * displayScale() >= 3 && Math.abs(end.y - d.p.y) * displayScale() >= 3 ? selectInRectangle(drawing.shapes, d.p, end) : []);
      for (const group of svg.querySelectorAll('[data-id]')) group.classList.toggle('is-selected', selection.has(group.dataset.id));
      return;
    }
    if (d.mode === 'draw') {
      const pointShape={id:'drawing-pointer',kind:'RECT',label:'',x:p.x,y:p.y,width:0,height:0,rotation:0};
      const guided=guidePlacement([pointShape],event,align(p.x)-p.x,align(p.y)-p.y)[0],end=bounded(guided);
      d.geometry = drawnGeometry(tool, d.p, end, drawing.width, drawing.height,drawing.infiniteCanvas);
      if(d.geometry&&!frameInside(d.geometry,drawing.width,drawing.height,drawing.infiniteCanvas))d.geometry=null;
      if(liveGuides){liveGuides.distances=[];if(d.geometry&&squareKinds.has(tool)){const b=shapeBounds({...d.geometry,kind:tool});liveGuides.guides=liveGuides.guides.filter(g=>(g.axis==='x'?[b.left,(b.left+b.right)/2,b.right]:[b.top,(b.top+b.bottom)/2,b.bottom]).some(v=>Math.abs(v-g.position)<1e-6));}}
      if (preview) preview.remove(); preview = null;
      if (d.geometry) { preview = element('g', { class: 'parking-draw-preview' }); preview.append(element(pathKinds.has(tool) ? 'path' : tool === 'WALL_CIRCLE' ? 'ellipse' : 'rect', { class: 'parking-body' })); geometry(preview, { ...d.geometry, kind: tool, label: '' }); svg.append(preview); }
      renderLiveGuides();
      return;
    }
    if (d.mode === 'move-group') {
      const dx=p.x-d.p.x,dy=p.y-d.p.y,raw=d.originals.map(s=>({...structuredClone(s),x:s.x+dx,y:s.y+dy})),moved=guidePlacement(raw,event,align(dx)-dx,align(dy)-dy);
      if(moved.every(s=>frameInside(s,drawing.width,drawing.height,drawing.infiniteCanvas)))for(const shape of moved){Object.assign(drawing.shapes.find(s=>s.id===shape.id),shape);geometry(svg.querySelector(`[data-id="${shape.id}"]`),shape);}else clearGuides();renderRulers();renderGroupControls();renderLiveGuides();
      return;
    }
    const shape = drawing.shapes.find(x => x.id === selected); if (!shape) return;
    const previous = structuredClone(shape);
    if (d.mode === 'rotate') { shape.rotation = normalizeAngle(Math.round((Math.atan2(p.y - shape.y - shape.height / 2, p.x - shape.x - shape.width / 2) * 180 / Math.PI + 90) * 10) / 10); if (event.shiftKey) shape.rotation = normalizeAngle(Math.round(shape.rotation / 15) * 15); }
    else if (d.mode === 'tail') Object.assign(shape, extendDivider(d.original, p, snap ? 10 : 1));
    else if (d.mode === 'control') {
      const points = d.original.controlPoints.map(v => worldPoint(d.original, v));
      points[d.control] = bounded({x:align(p.x),y:align(p.y)});
      if (Math.hypot(points[0].x - points.at(-1).x, points[0].y - points.at(-1).y) >= 4) Object.assign(shape, boundedPointFrame(points, drawing.width, drawing.height,drawing.infiniteCanvas));
    }
    else if (d.mode === 'vertex') {
      const o = d.original, angle = o.rotation * Math.PI / 180, c = Math.cos(angle), s = Math.sin(angle);
      const points = o.outline.map(v => { const x = (v.x - .5) * o.width, y = (v.y - .5) * o.height; return { x: o.x + o.width / 2 + x * c - y * s, y: o.y + o.height / 2 + x * s + y * c }; });
      points[d.vertex] = bounded({x:align(p.x),y:align(p.y)});
      const turns = points.map((a, i) => { const b = points[(i + 1) % 4], z = points[(i + 2) % 4]; return (b.x - a.x) * (z.y - b.y) - (b.y - a.y) * (z.x - b.x); });
      const x = Math.min(...points.map(v => v.x)), y = Math.min(...points.map(v => v.y)), w = Math.max(...points.map(v => v.x)) - x, h = Math.max(...points.map(v => v.y)) - y;
      if (w < 4 || h < 4 || !(turns.every(v => v > 1e-6) || turns.every(v => v < -1e-6))) return;
      Object.assign(shape, { x, y, width: w, height: h, rotation: 0, outline: points.map(v => ({ x: (v.x - x) / w, y: (v.y - y) / h })) });
    }
    else if (d.mode === 'move') {const x=d.original.x+p.x-d.p.x,y=d.original.y+p.y-d.p.y;Object.assign(shape,guidePlacement([{...d.original,x,y}],event,align(x)-x,align(y)-y)[0]);}
    else {
      const angle = d.original.rotation * Math.PI / 180, dx = p.x - d.p.x, dy = p.y - d.p.y;
      shape.width = Math.max(4, Math.min(drawing.infiniteCanvas?Infinity:drawing.width - shape.x, align(d.original.width + dx * Math.cos(angle) + dy * Math.sin(angle))));
      shape.height = Math.max(4, Math.min(drawing.infiniteCanvas?Infinity:drawing.height - shape.y, align(d.original.height - dx * Math.sin(angle) + dy * Math.cos(angle))));
      if (squareKinds.has(shape.kind)) shape.width = shape.height = Math.min(shape.width, shape.height);
    }
    if (!frameInside(shape, drawing.width, drawing.height,drawing.infiniteCanvas)){Object.assign(shape, previous);clearGuides();}
    geometry(d.group, shape);
    renderRulers();
    renderLiveGuides();
  }
  function up(event) {
    if (!dragging) return;
    const d = dragging; dragging = null;
    clearGuides();
    if (svg.hasPointerCapture(event.pointerId)) svg.releasePointerCapture(event.pointerId);
    if (d.mode === 'region') { marquee?.remove(); marquee = null; selectMany([...selection]); svg.focus({ preventScroll: true }); return; }
    if (d.mode === 'draw' && preview) {
      preview.remove(); preview = null;
      if (d.geometry) {
        const before = copyDrawing(drawing), label = tool === 'SPACE' ? nextSpaceLabel(drawing.shapes) : ['ENTRANCE', 'EXIT', 'GATE', 'LANE', 'LABEL'].includes(tool) ? shapeNames[tool] : '';
        const shape = { id: crypto.randomUUID(), kind: tool, label, ...d.geometry }; drawing.shapes.push(shape); selected = shape.id; commit(before);
      }
    } else if (d.before && JSON.stringify(d.before) !== JSON.stringify(drawing)) commit(d.before);
  }
  function action(name, input) {
    if (disposed) return;
    clearGuides();
    if(name==='smart-guides'){smartGuides=input!==false;return;}
    if (name === 'fit') { fit(); return; }
    if (name === 'zoom-in' || name === 'zoom-out') { const f = name === 'zoom-in' ? .8 : 1.25; zoom(f, { x: view.x + view.w / 2, y: view.y + view.h / 2 }); return; }
    if (name === 'tool') { tool = kinds.includes(input) || ['select', 'region', 'pan', 'calibrate'].includes(input) ? input : 'select'; scaleStart = null; if (tool === 'region') select(null); else render(); return; }
    if (!editable) return;
    if(name==='rotate-group') {
      const result=rotatedShapes(chosenShapes(),drawing.width,drawing.height,input,drawing.infiniteCanvas);
      if(result.error){notify('OnParkingRejected',result.error);return;}
      const before=copyDrawing(drawing),updates=new Map(result.shapes.map(s=>[s.id,s]));drawing.shapes=drawing.shapes.map(s=>updates.get(s.id)||s);
      if(JSON.stringify(before)!==JSON.stringify(drawing))commit(before);
      if(drawing.infiniteCanvas&&result.shapes.some(s=>{const b=shapeBounds(s);return b.left<view.x||b.top<view.y||b.right>view.x+view.w||b.bottom>view.y+view.h;}))fit();
      svg.focus({preventScroll:true});return;
    }
    if(name==='measure-axis'){measureAxis=input==='y'?'y':'x';renderRulers();notify('OnParkingSpacingChanged',spacingSummary());return;}
    if(name==='measure-unit'){measureUnit=input==='cm'?'cm':'m';renderRulers();notify('OnParkingSpacingChanged',spacingSummary());return;}
    if(name==='calibrate-shape') {
      const shape=chosenShapes()[0];if(!shape||!Number.isFinite(input)||input<=0){notify('OnParkingRejected','Chọn một hình có chiều rộng đã biết và nhập chiều rộng thực tế bằng mét.');return;}
      const before=copyDrawing(drawing);drawing.scale={start:worldPoint(shape,{x:0,y:.5}),end:worldPoint(shape,{x:1,y:.5}),length:input,unit:'m',confirmed:true};commit(before);return;
    }
    if(name==='arrange') {
      const {factor}=measurementScale(drawing,measureUnit);if(!factor){notify('OnParkingRejected','Đặt kích thước thực trước khi nhập khoảng cách bằng m/cm.');return;}
      const result=arrangeShapes(chosenShapes(),drawing.width,drawing.height,{axis:input?.axis,mode:input?.mode,gap:input?.gap/factor,infiniteCanvas:drawing.infiniteCanvas});
      if(result.error){notify('OnParkingRejected',result.error);return;}
      const before=copyDrawing(drawing),updates=new Map(result.shapes.map(s=>[s.id,s]));
      drawing.shapes=drawing.shapes.map(s=>updates.get(s.id)||s);measureAxis=input.mode==='align'?(input.axis==='y'?'x':'y'):input.axis;
      if(JSON.stringify(before)!==JSON.stringify(drawing))commit(before);else{renderRulers();notify('OnParkingSpacingChanged',spacingSummary());}
      if(drawing.infiniteCanvas&&result.shapes.some(s=>{const b=shapeBounds(s);return b.left<view.x||b.top<view.y||b.right>view.x+view.w||b.bottom>view.y+view.h;}))fit();
      svg.focus({preventScroll:true});return;
    }
    if (name === 'grid') { snap = !snap; render(); return; }
    if (name === 'clear-selection') { select(null); return; }
    if (name === 'select-ids') { selectMany(Array.isArray(input)?input:[]);svg.focus({preventScroll:true});return; }
    if (name === 'copy') {
      if(!selection.size)return;
      clipboard=drawing.shapes.filter(s=>selection.has(s.id)&&s.kind!=='TRACE').map(s=>structuredClone(s));pasteCount=0;
      notify('OnParkingClipboardChanged',clipboard.length);svg.focus({preventScroll:true});return;
    }
    if (name === 'paste' || name === 'duplicate') {
      const templates=name==='paste'?clipboard:drawing.shapes.filter(s=>selection.has(s.id)&&s.kind!=='TRACE');
      if(!templates.length)return;
      const result=pasteShapes(drawing,templates,20/displayScale()*(name==='paste'?pasteCount+1:1));
      if(result.error){notify('OnParkingRejected',result.error);return;}
      const before=copyDrawing(drawing);drawing.shapes.push(...result.shapes);
      selection=new Set(result.shapes.map(s=>s.id));selected=selection.size===1?[...selection][0]:null;
      if(name==='paste')pasteCount++;commit(before);svg.focus({preventScroll:true});return;
    }
    if (name === 'delete') { if (!selection.size) return; const before = copyDrawing(drawing); drawing.shapes = drawing.shapes.filter(s => !selection.has(s.id)); selection.clear(); selected = null; commit(before); svg.focus({ preventScroll: true }); return; }
    if (name === 'undo' || name === 'redo') { const from = name === 'undo' ? undo : redo, to = name === 'undo' ? redo : undo; if (!from.length) return; to.push(copyDrawing(drawing)); drawing = from.pop(); selected = null; selection.clear(); tool = 'select'; render(); notify('OnParkingChanged', drawing, selected); notifySelection(); return; }
    if (name === 'confirm-reviewed') { const before = copyDrawing(drawing); drawing.shapes.forEach(s => { if (s.kind === 'SPACE') s.needsReview = false; }); commit(before); return; }
    if (name === 'scale') { if (!drawing.scale || !Number.isFinite(input.length) || input.length <= 0 || !['mm', 'cm', 'm'].includes(input.unit)) return; const before = copyDrawing(drawing); Object.assign(drawing.scale, input, { confirmed: true }); commit(before); return; }
    if (name === 'clear-scale') { const before = copyDrawing(drawing); drawing.scale = null; commit(before); return; }
    const shape = drawing.shapes.find(x => x.id === selected); if (!shape) return;
    const before = copyDrawing(drawing);
    if (name === 'update') {
      if (kinds.includes(input.kind) && input.kind !== shape.kind) { shape.kind = input.kind; shape.outline = null; shape.contours = null; shape.controlPoints = input.kind === 'LINE' ? [{ x: 0, y: .5 }, { x: 1, y: .5 }] : input.kind === 'CURVE' ? [{ x: 0, y: 1 }, { x: .5, y: 0 }, { x: 1, y: 1 }] : null; }
      for (const key of ['label', 'x', 'y', 'width', 'height', 'rotation', 'needsReview']) if (Object.hasOwn(input, key)) shape[key] = input[key];
      shape.rotation = normalizeAngle(shape.rotation);
      if (squareKinds.has(shape.kind)) shape.height = shape.width;
    }
    else return;
    if (drawing.shapes.some(s => !frameInside(s, drawing.width, drawing.height,drawing.infiniteCanvas))) { drawing = before; render(); notify('OnParkingRejected', 'Vị trí hoặc kích thước chưa hợp lệ. Hãy kiểm tra đơn vị m/cm và giữ hai đầu đường vẽ khác nhau.'); return; }
    commit(before);
  }
  function zoom(factor, p) { const width = view.w * factor; if (width < 4 || width > (drawing.infiniteCanvas?2e9:drawing.width*8)) return; view = { x: p.x - (p.x - view.x) * factor, y: p.y - (p.y - view.y) * factor, w: width, h: view.h * factor }; viewport(); }
  on(svg, 'pointerdown', down); on(svg, 'pointermove', move); on(svg, 'pointerup', up);
  on(window, 'resize', viewport);
  on(svg, 'pointercancel', event => { clearGuides();if (dragging?.before) drawing = dragging.before; const previous = dragging?.previousSelection; dragging = null; preview = null; marquee?.remove(); marquee = null; if (previous) selectMany(previous); else render(); });
  on(svg, 'wheel', event => { event.preventDefault();if (event.ctrlKey||event.metaKey) zoom(event.deltaY < 0 ? .9 : 1.1, point(event));else{const scale=displayScale();view.x+=(event.deltaX+(event.shiftKey?event.deltaY:0))/scale;view.y+=(event.shiftKey?0:event.deltaY)/scale;viewport();} }, { passive: false });
  on(svg, 'keydown', event => {
    if (event.key === 'Enter' && event.target.closest('[data-id]')) { const id=event.target.closest('[data-id]').dataset.id;if(editable&&event.shiftKey){const ids=new Set(selection);if(ids.has(id))ids.delete(id);else ids.add(id);selectMany([...ids]);}else select(id);event.preventDefault();return; }
    if (!editable) return;
    if(event.target.closest('[data-group-rotate]')&&['ArrowLeft','ArrowRight'].includes(event.key)){event.preventDefault();action('rotate-group',(event.key==='ArrowLeft'?-1:1)*(event.shiftKey?15:1));svg.querySelector('[data-group-rotate]')?.focus({preventScroll:true});return;}
    if (event.key === 'Delete' || event.key === 'Backspace') { event.preventDefault(); action('delete'); }
    else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') { event.preventDefault(); action(event.shiftKey ? 'redo' : 'undo'); }
    else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'y') { event.preventDefault(); action('redo'); }
    else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'd') { event.preventDefault(); action('duplicate'); }
    else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'c') { event.preventDefault(); action('copy'); }
    else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'v') { event.preventDefault(); action('paste'); }
    else if (event.key === 'Escape') { clearGuides();if (dragging?.before) drawing = dragging.before; dragging = null; preview?.remove(); preview = null; marquee?.remove(); marquee = null; select(null); }
  });
  fit(); render();
  return {
    action,

    setReference(url) { referenceImage = typeof url === 'string' && url.startsWith('blob:') ? url : null; render(); },
    load(next, states, edit) { clearGuides();drawing = copyDrawing(next); spaces = states || []; editable = edit; selection = new Set([...selection].filter(id => drawing.shapes.some(x => x.id === id))); selected = selection.size === 1 ? [...selection][0] : null; undo.length = redo.length = 0; fit(); render(); },
    dispose() { disposed = true; listeners.forEach(off => off()); root.replaceChildren(); }
  };
}
