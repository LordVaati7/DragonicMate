import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const source = await readFile(new URL('../../src/OpenGameMate.Adapters/ChatGptComposer.js', import.meta.url), 'utf8');
// Decode the literal selectors from the production BuiltIn initializer, rather
// than maintaining a second set of rules which can hide production regressions.
const ruleSource = await readFile(new URL('../../src/OpenGameMate.Adapters/ChatGptAdapterRules.cs', import.meta.url), 'utf8');
const initializer = ruleSource.split('BuiltIn { get; } = new()')[1]?.split('\n    };')[0];
assert.ok(initializer, 'Production BuiltIn rule initializer must be available');
const literal = '"(?:[^"\\\\]|\\\\.)*"';
const rules = Object.fromEntries([
  'ComposerSelector', 'FileInputSelector', 'SendButtonSelector', 'BusyButtonSelector',
  'AttachmentPreviewSelectors', 'QuotaErrorSelectors', 'PlatformErrorSelectors'
].map(name => {
  const array = name.endsWith('Selectors');
  const match = initializer.match(new RegExp(`${name}\\s*=\\s*(${array ? '\\[[\\s\\S]*?\\n\\s*\\]' : literal})`));
  assert.ok(match, `Production rule ${name} must use literal selectors`);
  const value = array ? [...match[1].matchAll(new RegExp(literal, 'g'))].map(value => JSON.parse(value[0]))
    : JSON.parse(match[1]);
  assert.ok(value.length > 0, `Production rule ${name} cannot be empty`);
  return [name, value];
}));
const editor = '<div id="prompt-textarea" contenteditable="true" role="textbox"></div>';
const send = '<button data-testid="send-button" type="button">Send</button>';
const preview = '<div data-testid="file-thumbnail">Synthetic image</div>';
const dataImage = 'data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="80" height="45"%3E%3C/svg%3E';
let browser;
before(async () => { browser = await chromium.launch(); });
after(async () => { await browser?.close(); });

async function fixture(t, body = `<form id="active">${editor}${send}</form>`, url = 'https://chatgpt.com/') {
  const context = await browser.newContext({ serviceWorkers: 'block' });
  t.after(() => context.close());
  await context.route('**/*', route => route.request().url() === url
    ? route.fulfill({ contentType: 'text/html', body: `<!doctype html><style>[contenteditable]{min-height:30px}</style>${body}` })
    : route.abort());
  const page = await context.newPage();
  await page.goto(url);
  const run = (action, extra = {}) => page.evaluate(`(${source})(${JSON.stringify({
    action, rules, operationId: 'test-operation', ...extra
  })})`);
  return { page, run };
}

async function prepare(t, { image = true, voice = false } = {}) {
  const f = await fixture(t, `<form id="active">${editor}${send}</form>${voice
    ? '<button data-testid="end-voice-mode-button">End Voice</button>' : ''}`);
  await f.run('begin', { requireImage: image });
  if (image) await f.page.locator('form').evaluate((form, html) => form.insertAdjacentHTML('beforeend', html), preview);
  assert.equal((await f.run('text', { message: 'Synthetic prompt' })).textInserted, true);
  return f;
}

test('three global inputs: choose only the one associated with the active form', async t => {
  const { page, run } = await fixture(t, `<input type="file" accept="image/*"><form id="active">${editor}
    <input id="correct" type="file" accept="image/*" hidden>${send}</form><input type="file" accept="image/*">`);
  const result = await run('begin', { requireImage: true });
  assert.equal(result.code, 'prepare-c1-f3-s1');
  assert.equal(result.method, 'file-input');
  assert.equal(await page.locator('[data-opengamemate-file]').getAttribute('id'), 'correct');
});

test('explicit HTML form association works outside the form tree', async t => {
  const { page, run } = await fixture(t, `<form id="active">${editor}${send}</form>
    <input id="associated" form="active" type="file" accept="image/*" hidden>`);
  assert.equal((await run('begin', { requireImage: true })).method, 'file-input');
  assert.equal(await page.locator('[data-opengamemate-file]').getAttribute('id'), 'associated');
});

for (const count of [2, 4]) {
  test(`${count} associated inputs: paste only into the composer, never choose an input`, async t => {
    const { page, run } = await fixture(t, `<form>${editor}${send}${'<input type="file" hidden>'.repeat(count)}</form>`);
    await page.locator('#prompt-textarea').evaluate(element => element.addEventListener('paste', event => {
      window.pastes = (window.pastes ?? 0) + 1;
      window.pasteTarget = event.target.id;
      element.closest('form').insertAdjacentHTML('beforeend', '<div data-testid="file-thumbnail">Synthetic image</div>');
    }));
    const begin = await run('begin', { requireImage: true });
    assert.equal(begin.code, `prepare-c1-f${count}-s${count}`);
    assert.equal(begin.method, 'composer-paste');
    assert.equal(await page.evaluate(() => window.pastes ?? 0), 0);
    assert.equal((await run('paste', { imageBase64: 'AA==' })).code, 'paste-dispatched');
    assert.equal(await page.locator('[data-opengamemate-file]').count(), 0);
    assert.equal(await page.locator('input').evaluateAll(inputs => inputs.every(input => input.files.length === 0)), true);
    assert.equal(await page.evaluate(() => window.pasteTarget), 'prompt-textarea');
    assert.equal((await run('attachment')).previewDetected, true);
    assert.equal((await run('text', { message: 'Synthetic prompt' })).textInserted, true);
  });
}

test('rejected paste is attempted once and never treated as a prepared image', async t => {
  const { page, run } = await fixture(t, `<form>${editor}${send}${'<input type="file">'.repeat(4)}</form>`);
  await page.evaluate(() => { window.pastes = 0; document.addEventListener('paste', () => window.pastes++); });
  await run('begin', { requireImage: true });
  await run('paste', { imageBase64: 'AA==' });
  assert.equal((await run('paste', { imageBase64: 'AA==' })).code, 'paste-not-permitted');
  assert.equal(await page.evaluate(() => window.pastes), 1);
  assert.equal((await run('attachment')).previewDetected, false);
  assert.equal((await run('text', { message: 'Synthetic prompt' })).code, 'attachment-not-ready');
  assert.equal((await run('invoke')).invoked, undefined);
});

test('unassociated inputs are untouched; paste targets the unique composer only', async t => {
  const { page, run } = await fixture(t, `<input type="file"><form>${editor}${send}</form><input type="file">`);
  await page.locator('#prompt-textarea').evaluate(element => element.addEventListener('paste', event => {
    window.pasteTarget = event.target.id;
    window.pasteFiles = event.clipboardData.files.length;
  }));
  assert.equal((await run('begin', { requireImage: true })).method, 'composer-paste');
  assert.equal((await run('paste', { imageBase64: 'AA==' })).code, 'paste-dispatched');
  assert.equal(await page.evaluate(() => window.pasteTarget), 'prompt-textarea');
  assert.equal(await page.evaluate(() => window.pasteFiles), 1);
  assert.equal(await page.locator('[data-opengamemate-file]').count(), 0);
  assert.equal((await run('attachment')).previewDetected, false);
});

test('hidden duplicate compositor is ignored', async t => {
  const { run } = await fixture(t, `<div hidden>${editor}</div><form>${editor}${send}</form>`);
  assert.match((await run('begin')).code, /^prepare-c1/);
});

test('multiple visible composers fail closed', async t => {
  const { run } = await fixture(t, `<form>${editor}${send}</form><form>${editor}${send}</form>`);
  assert.equal((await run('begin')).code, 'composer-count-2');
});

test('unique visible ARIA contenteditable is supported without the old ID', async t => {
  const { run } = await fixture(t, `<form><div contenteditable="true" role="textbox" style="height:30px"></div>${send}</form>`);
  assert.match((await run('begin')).code, /^prepare-c1/);
  assert.equal((await run('text', { message: 'Synthetic prompt' })).textInserted, true);
});

test('draft is preserved and its contents never leave the page', async t => {
  const { page, run } = await fixture(t, `<form>${editor.replace('></div>', '>PRIVATE_DRAFT</div>')}${send}</form>`);
  const result = await run('begin', { requireImage: true });
  assert.equal(result.code, 'composer-draft-present');
  assert.equal(JSON.stringify(result).includes('PRIVATE_DRAFT'), false);
  assert.equal(await page.locator('#prompt-textarea').innerText(), 'PRIVATE_DRAFT');
  assert.equal((await run('idle')).composerEmpty, false);
});

test('existing attachments are never adopted', async t => {
  const { run } = await fixture(t, `<form>${editor}${send}${preview}</form>`);
  assert.equal((await run('begin', { requireImage: true })).code, 'composer-attachment-present');
});

test('file selection alone is not a visible attachment', async t => {
  const { page, run } = await fixture(t, `<form>${editor}${send}<input type="file"></form>`);
  await run('begin', { requireImage: true });
  await page.locator('input').setInputFiles({ name: 'synthetic.png', mimeType: 'image/png', buffer: Buffer.from([0]) });
  const result = await run('attachment');
  assert.equal(result.fileSelected, true);
  assert.equal(result.previewDetected, false);
  assert.equal((await run('text', { message: 'Synthetic prompt' })).code, 'attachment-not-ready');
});

test('preview elsewhere in the page is not accepted', async t => {
  const { run } = await fixture(t, `<form>${editor}${send}</form>${preview}`);
  await run('begin', { requireImage: true });
  assert.equal((await run('attachment')).previewDetected, false);
});

test('upload in progress prevents text insertion and invocation', async t => {
  const { page, run } = await fixture(t);
  await run('begin', { requireImage: true });
  await page.locator('form').evaluate((form, html) => form.insertAdjacentHTML('beforeend', html), `${preview}<progress></progress>`);
  assert.equal((await run('attachment')).uploading, true);
  assert.equal((await run('text', { message: 'Synthetic prompt' })).code, 'attachment-not-ready');
});

test('timeout distinguishes an unrecognized image from upload activity without exposing content', async t => {
  const { page, run } = await fixture(t);
  await run('begin', { requireImage: true });
  await page.locator('form').evaluate(form => form.insertAdjacentHTML('beforeend',
    '<img src="https://private.invalid/PRIVATE_IMAGE" alt="PRIVATE_LABEL" style="width:80px;height:80px">'));
  const timeout = await run('attachment-timeout');
  assert.equal(timeout.code, 'attachment-timeout-p0-u0-i1-b0-d0-h1-s1-e1');
  assert.equal(timeout.previewDetected, false);
  assert.equal(timeout.diagnostics.failureStage, 'Readiness');
  assert.equal(JSON.stringify(timeout).includes('PRIVATE'), false);
  assert.equal((await run('text', { message: 'Synthetic prompt' })).code, 'attachment-not-ready');
  assert.equal((await run('invoke')).invoked, undefined);
  await page.locator('form').evaluate((form, html) => form.insertAdjacentHTML('beforeend', html),
    `${preview}<progress></progress>`);
  assert.equal((await run('attachment-timeout')).code, 'attachment-timeout-p1-u1-i1-b0-d0-h1-s1-e1');
});

test('timeout counts neither hidden images nor images outside the active form', async t => {
  const { page, run } = await fixture(t, `<img src="https://private.invalid/OUTSIDE"><form>${editor}${send}</form>`);
  await run('begin', { requireImage: true });
  await page.locator('form').evaluate(form => form.insertAdjacentHTML('beforeend',
    '<img hidden src="https://private.invalid/HIDDEN">'));
  assert.equal((await run('attachment-timeout')).code, 'attachment-timeout-p0-u0-i0-b0-d0-h0-s1-e1');
});

test('clear file input does not confirm an image submission while preview remains', async t => {
  const { page, run } = await prepare(t);
  await run('invoke');
  await page.locator('#prompt-textarea').fill('');
  const result = await run('post');
  assert.equal(result.composerCleared, true);
  assert.equal(result.attachmentCleared, false);
  assert.equal(result.consumed, false);
});

test('image submission requires both composer and preview to clear', async t => {
  const { page, run } = await prepare(t);
  await run('invoke');
  await page.locator('[data-testid="file-thumbnail"]').evaluate(element => element.remove());
  assert.equal((await run('post')).consumed, false);
  await page.locator('#prompt-textarea').fill('');
  assert.equal((await run('post')).consumed, true);
});

test('role-only submission requires the composer to clear', async t => {
  const { page, run } = await prepare(t, { image: false });
  await run('invoke');
  assert.equal((await run('post')).consumed, false);
  await page.locator('#prompt-textarea').fill('');
  assert.equal((await run('post')).consumed, true);
});

test('a changed draft is not submitted', async t => {
  const { page, run } = await prepare(t);
  await page.locator('#prompt-textarea').fill('New user draft');
  assert.equal((await run('invoke')).code, 'prepared-text-changed');
});

test('navigation and composer replacement invalidate prepared input', async t => {
  const { page, run } = await prepare(t);
  await page.evaluate(() => history.pushState({}, '', '/c/other-conversation'));
  assert.equal((await run('invoke')).code, 'prepared-context-changed');
});

test('replacement with identical markup does not adopt the new composer', async t => {
  const { page, run } = await prepare(t);
  await page.locator('#prompt-textarea').evaluate(element => { element.outerHTML = element.outerHTML; });
  assert.equal((await run('invoke')).code, 'prepared-context-changed');
});

test('a submit click is latched and cannot be repeated', async t => {
  const { page, run } = await prepare(t);
  await page.locator('button').evaluate(button => button.addEventListener('click', () => { window.clicks = (window.clicks ?? 0) + 1; }));
  assert.equal((await run('invoke')).invoked, true);
  assert.equal((await run('invoke')).code, 'already-invoked');
  assert.equal(await page.evaluate(() => window.clicks), 1);
});

test('quota and disabled send controls are never invoked', async t => {
  const { page, run } = await prepare(t);
  await page.locator('button').evaluate(button => { button.disabled = true; });
  assert.equal((await run('invoke')).invoked, undefined);
  await page.locator('button').evaluate(button => { button.disabled = false; });
  await page.locator('form').evaluate(form => form.insertAdjacentHTML('beforeend', '<div data-testid="quota-error">Synthetic limit</div>'));
  assert.equal((await run('invoke')).quotaDetected, true);
});

test('visible stop control prevents invocation', async t => {
  const { page, run } = await prepare(t);
  await page.locator('form').evaluate(form => form.insertAdjacentHTML('beforeend', '<button data-testid="stop-button">Stop</button>'));
  assert.equal((await run('invoke')).code, 'send-control-changed-before-invoke');
});

test('manual preparation supports a stable open Voice session without enabling automatic sends', async t => {
  const { page, run } = await prepare(t, { voice: true });
  assert.equal((await run('control')).diagnostics.pageState, 'VoiceComposerWithAttachment');
  await page.locator('[data-testid="end-voice-mode-button"]').evaluate(element => element.remove());
  assert.equal((await run('invoke')).code, 'voice-session-changed');
});

test('non-official origins fail before reading or mutating composer', async t => {
  const { run } = await fixture(t, `<form>${editor}${send}</form>`, 'https://example.com/');
  assert.equal((await run('begin')).code, 'unexpected-origin');
});

async function addDataPreview(page) {
  await page.locator('form').evaluate((form, src) => {
    const image = document.createElement('img');
    image.src = src;
    form.append(image);
  }, dataImage);
  await page.waitForFunction(() => {
    const image = document.querySelector('form img');
    return image?.complete && image.naturalWidth > 0;
  });
}

test('real rules handle the observed data image and five unlabelled buttons through one submission', async t => {
  const { page, run } = await fixture(t, `<form>${editor}${'<button type="button">Other</button>'.repeat(4)}
    <button type="submit">Send</button>${'<input type="file" hidden>'.repeat(4)}</form>`);
  const png = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 80;
    canvas.height = 45;
    return canvas.toDataURL('image/png');
  });
  await page.evaluate(src => {
    document.querySelector('#prompt-textarea').addEventListener('paste', event => {
      event.preventDefault();
      window.pastes = (window.pastes ?? 0) + 1;
      const image = document.createElement('img');
      image.src = src;
      event.target.closest('form').append(image);
    });
    document.querySelector('form').addEventListener('submit', event => {
      event.preventDefault();
      window.submissions = (window.submissions ?? 0) + 1;
      window.submittedText = document.querySelector('#prompt-textarea').innerText;
      window.submittedImages = event.target.querySelectorAll('img').length;
      document.querySelector('#prompt-textarea').replaceChildren();
      event.target.querySelector('img').remove();
      history.pushState({}, '', '/c/synthetic-created-conversation');
    });
  }, png);
  assert.equal((await run('begin', { requireImage: true })).method, 'composer-paste');
  await run('paste', { imageBase64: 'AA==' });
  await page.waitForFunction(() => document.querySelector('form img')?.naturalWidth > 0);
  const attachment = await run('attachment');
  assert.equal(attachment.previewDetected, true);
  assert.equal(attachment.uploading, false);
  assert.equal((await run('text', { message: 'Synthetic prompt' })).textInserted, true);
  assert.equal((await run('control')).sendButtonCount, 1);
  assert.equal((await run('invoke')).invoked, true);
  assert.equal((await run('post')).consumed, true);
  assert.equal((await run('invoke')).invoked, undefined);
  assert.deepEqual(await page.evaluate(() => [window.pastes, window.submissions,
    window.submittedImages, window.submittedText]), [1, 1, 1, 'Synthetic prompt']);
});

test('pre-existing data images are preserved and never adopted', async t => {
  const { page, run } = await fixture(t);
  await addDataPreview(page);
  assert.equal((await run('begin', { requireImage: true })).code, 'composer-attachment-present');
  assert.equal(await page.locator('form img').count(), 1);
});

test('a data image outside the active form cannot unlock submission', async t => {
  const { page, run } = await fixture(t);
  await run('begin', { requireImage: true });
  await page.evaluate(src => { const image = new Image(); image.src = src; document.body.append(image); }, dataImage);
  assert.equal((await run('attachment')).previewDetected, false);
  assert.equal((await run('text', { message: 'Synthetic prompt' })).code, 'attachment-not-ready');
});

test('broken data images cannot be treated as ready attachments', async t => {
  const { page, run } = await fixture(t);
  await run('begin', { requireImage: true });
  await page.locator('form').evaluate(form => form.insertAdjacentHTML('beforeend',
    '<img src="data:image/png;base64,AA==" style="width:80px;height:45px">'));
  assert.equal((await run('attachment')).uploading, true);
  assert.equal((await run('text', { message: 'Synthetic prompt' })).code, 'attachment-not-ready');
});

test('duplicate generic submit buttons prevent any click', async t => {
  const { page, run } = await fixture(t, `<form>${editor}<button type="submit">One</button><button type="submit">Two</button></form>`);
  await run('begin', { requireImage: true });
  await addDataPreview(page);
  await run('text', { message: 'Synthetic prompt' });
  assert.equal((await run('control')).sendButtonCount, 2);
  assert.equal((await run('invoke')).invoked, undefined);
});

test('an identified send button plus a distinct submit button also remains ambiguous', async t => {
  const { page, run } = await prepare(t);
  await page.locator('form').evaluate(form => form.insertAdjacentHTML('beforeend', '<button type="submit">Other</button>'));
  assert.equal((await run('control')).sendButtonCount, 2);
  assert.equal((await run('invoke')).invoked, undefined);
});

for (const attribute of ['formaction', 'formmethod', 'formtarget', 'form']) {
  test(`a submit button with ${attribute} reassignment is ineligible`, async t => {
    const { page, run } = await fixture(t, `<form id="active">${editor}<button type="submit" ${attribute}="other">Other</button></form><form id="other"></form>`);
    await run('begin', { requireImage: true });
    await page.locator('#active').evaluate((form, html) => form.insertAdjacentHTML('beforeend', html), preview);
    await run('text', { message: 'Synthetic prompt' });
    assert.equal((await run('control')).sendButtonCount, 0);
    assert.equal((await run('invoke')).invoked, undefined);
  });
}

test('matching both send selectors still counts as one button', async t => {
  const { page, run } = await prepare(t);
  await page.locator('[data-testid="send-button"]').evaluate(button => {
    button.type = 'submit';
    button.addEventListener('click', event => event.preventDefault());
  });
  assert.equal((await run('control')).sendButtonCount, 1);
  assert.equal((await run('invoke')).invoked, true);
});

test('an unrelated navigation after a click cannot confirm submission', async t => {
  const { page, run } = await prepare(t);
  await run('invoke');
  await page.locator('#prompt-textarea').fill('');
  await page.locator('[data-testid="file-thumbnail"]').evaluate(element => element.remove());
  await page.evaluate(() => history.pushState({}, '', '/settings'));
  assert.equal((await run('post')).code, 'prepared-context-changed');
});
