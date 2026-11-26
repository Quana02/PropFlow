import assert from 'node:assert/strict';
import { displaySize, pdfRectToDisplay, displayRectToPdf, clampDisplayRect } from '../src/PropFlow.Web.Client/wwwroot/Features/RemoteOnlineSign/Pages/remote-pdf-geometry.mjs';
const cases=[{x:72,y:96,width:180,height:60},{x:0,y:0,width:40,height:30},{x:350,y:500,width:100,height:50}];
for(const rotation of [0,90,180,270]) for(const source of cases){const shown=pdfRectToDisplay(rotation,595,842,source),roundTrip=displayRectToPdf(rotation,595,842,shown);for(const key of ['x','y','width','height'])assert.equal(roundTrip[key],source[key],`rotation ${rotation}: ${key}`);const size=displaySize(rotation,595,842);const bounded=clampDisplayRect({...shown,left:-10,top:-10},size.width,size.height);assert.ok(bounded.left>=0&&bounded.top>=0);}
console.log('Remote PDF viewer geometry: PASS (12 rotation/round-trip cases)');
