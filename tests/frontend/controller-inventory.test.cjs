const test = require('node:test');
const assert = require('node:assert/strict');
const parser = import('../../scripts/authz/controller-inventory.mjs');
test('controller without a class route retains action templates and constraints', async () => {
  const {controllerOperations: parse} = await parser;
  const result=parse('[HttpGet("api/v1/utility-insights/apartments/{apartment:guid}")]\npublic Task<IActionResult> Detail(Guid apartment) => null;', '[Authorize]', 'UtilityConsumptionController');
  assert.deepEqual(result.map(x=>x.method+' '+x.path), ['GET /api/v1/utility-insights/apartments/{apartment:guid}']);
});
test('array response metadata does not hide the action', async () => {
  const {controllerOperations: parse} = await parser;
  assert.equal(parse('[HttpGet("api/resident"), Authorize(Roles="RESIDENT")]\n[ProducesResponseType(typeof(View[]), 200)]\npublic Task<IActionResult> Resident() => null;', '[Authorize]', 'UtilityController')[0].path, '/api/resident');
});
test('absolute action routes override prefixes and aliases are deduplicated', async () => {
  const {controllerOperations: parse} = await parser;
  const source='[HttpPost("~/api/fixed/{id:int}")]\npublic IActionResult Save(int id) => null;\n[HttpGet, Route("[action]"), Route("alias")]\npublic IActionResult Read() => null;';
  const result=parse(source, '[Route("api/[controller]"), Route("api/alias")]', 'ExampleController');
  assert.deepEqual(result.map(x=>x.method+' '+x.path), ['POST /api/fixed/{id:int}', 'GET /api/Example/Read', 'GET /api/alias/Read', 'GET /api/Example/alias', 'GET /api/alias/alias']);
});
