// Embedded in the adapter and exercised against synthetic pages in CI.
// Return only control metadata and booleans, never composer text or page content.
(request) => {
  const { action, rules, operationId } = request;
  const key = Symbol.for('OpenGameMate.preparedInput');
  const visible = element => element.isConnected && element.getClientRects().length > 0 &&
    getComputedStyle(element).visibility !== 'hidden' &&
    !element.closest('[hidden],[inert],[aria-hidden="true"]');
  const enabled = element => !element.disabled &&
    element.getAttribute('aria-disabled') !== 'true';
  const text = element => element instanceof HTMLTextAreaElement ||
    element instanceof HTMLInputElement ? element.value : element.innerText;
  const fail = code => ({ code, diagnostics: {
    pageState: 'Unknown', detectedButtonCount: 0, candidateButtons: [],
    failureStage: 'ComposerScope'
  } });
  if (location.protocol !== 'https:' || location.port ||
      !(location.hostname === 'chatgpt.com' || location.hostname.endsWith('.chatgpt.com'))) {
    return fail('unexpected-origin');
  }

  const composers = [...new Set(document.querySelectorAll(rules.ComposerSelector))]
    .filter(element => visible(element) && enabled(element) &&
      (element instanceof HTMLTextAreaElement || element.isContentEditable));
  if (composers.length !== 1) return fail(`composer-count-${composers.length}`);
  const composer = composers[0];
  const form = composer.closest('form');
  if (!form) return fail('composer-form-missing');
  const matches = selectors => [...new Set(selectors.flatMap(selector =>
    [...form.querySelectorAll(selector)]))].filter(visible);
  const attachments = () => matches(rules.AttachmentPreviewSelectors);
  const hasAttachment = () => attachments().length > 0;
  const quota = () => rules.QuotaErrorSelectors.some(selector =>
    [...document.querySelectorAll(selector)].some(visible));
  const platformError = () => rules.PlatformErrorSelectors.some(selector =>
    [...document.querySelectorAll(selector)].some(visible));
  const uploading = () => {
    const images = [...new Set(attachments().flatMap(element =>
      element instanceof HTMLImageElement ? [element] : [...element.querySelectorAll('img')]))]
      .filter(visible);
    return matches(['[aria-busy="true"]', '[role="progressbar"]', 'progress']).length > 0 ||
      images.some(image => !image.complete || image.naturalWidth === 0);
  };
  // Both selectors are scoped to the unique composer's form. A submit button
  // associated with another form or an alternate endpoint is never eligible.
  // Multiple candidates remain ambiguous; never prefer the first one.
  const sendButtons = () => [...form.querySelectorAll(rules.SendButtonSelector)]
    .filter(button => visible(button) && button.form === form &&
      !button.hasAttribute('formaction') && !button.hasAttribute('formmethod') &&
      !button.hasAttribute('formtarget'));
  const stopButtons = () => [...document.querySelectorAll(rules.BusyButtonSelector)].filter(visible);
  const voiceOpen = () => [...document.querySelectorAll(
    'button[data-testid="end-voice-mode-button"],[data-testid="voice-mode-composer"]')].some(visible);
  const pageState = () => voiceOpen()
    ? (hasAttachment() ? 'VoiceComposerWithAttachment' : 'VoiceComposer')
    : (hasAttachment() ? 'ComposerWithAttachment' : 'Composer');
  const diagnostics = stage => {
    const buttons = [...form.querySelectorAll('button')].filter(visible);
    return { pageState: pageState(), detectedButtonCount: buttons.length,
      candidateButtons: buttons.slice(0, 12).map(button => ({
        tagName: 'button', scopeDepth: 0, type: button.getAttribute('type'),
        role: button.getAttribute('role'), disabled: Boolean(button.disabled),
        ariaDisabled: button.getAttribute('aria-disabled') === 'true',
        // Labels can contain user text in third-party controls; omit them from this probe.
        ariaLabel: null, dataTestId: button.getAttribute('data-testid')
      })), failureStage: stage };
  };
  const result = (code, values = {}, stage = 'None') => ({ code, ...values,
    quotaDetected: quota(), platformErrorDetected: platformError(),
    diagnostics: diagnostics(stage) });

  if (action === 'idle') {
    const sends = sendButtons();
    const stops = stopButtons();
    return result(text(composer).trim() ? 'composer-draft-present' : 'ok', {
      domainCorrect: true, composerCount: 1, stopButtonCount: stops.length,
      sendButtonCount: sends.length, sendButtonDisabled: sends.length === 1 && !enabled(sends[0]),
      sendButtonInComposerForm: sends.length === 1,
      composerEmpty: !text(composer).trim()
    });
  }

  if (action === 'begin') {
    // Never overwrite a draft or adopt an attachment left by another operation.
    if (text(composer).trim()) return result('composer-draft-present', {}, 'Readiness');
    if (hasAttachment()) return result('composer-attachment-present', {}, 'Readiness');
    if (stopButtons().length) return result('conversation-busy', {}, 'Readiness');
    const files = [...document.querySelectorAll(rules.FileInputSelector)].filter(element =>
      element instanceof HTMLInputElement && element.type === 'file' && enabled(element) &&
      (!element.accept || /image|png|jpe?g|gif/i.test(element.accept)));
    const scopedFiles = files.filter(element => element.form === form);
    // Multiple upload inputs are not interchangeable. In that case leave every
    // input untouched and use only the unique visible composer's paste handler.
    const state = { operationId, composer, form, href: location.href,
      requireImage: Boolean(request.requireImage), file: scopedFiles.length === 1 ? scopedFiles[0] : null,
      expectedText: null, invoked: false, attachmentAttempted: false, voiceOpen: voiceOpen() };
    window[key] = state;
    // Mark only an explicitly form-associated input. Other inputs remain untouched.
    if (state.requireImage && state.file) {
      state.file.setAttribute('data-opengamemate-file', operationId);
    }
    return result(`prepare-c1-f${files.length}-s${scopedFiles.length}`, {
      method: state.file ? 'file-input' : 'composer-paste'
    });
  }

  const state = window[key];
  // The first submission can turn the new-chat root into a conversation URL.
  // Allow only post-click observation across that transition, with the same
  // origin and the exact same composer/form. Never permit another invocation.
  const newChatSubmitted = action === 'post' && state?.invoked &&
    new URL(state.href).origin === location.origin &&
    new URL(state.href).pathname === '/' && /^\/c\/[^/]+$/.test(location.pathname);
  if (!state || state.operationId !== operationId || state.composer !== composer ||
      state.form !== form || (state.href !== location.href && !newChatSubmitted)) {
    return result('prepared-context-changed', {}, 'Readiness');
  }

  if (action === 'paste') {
    if (!state.requireImage || state.file || state.attachmentAttempted) {
      return result('paste-not-permitted', {}, 'Readiness');
    }
    if (state.invoked || text(composer).trim() || hasAttachment() || stopButtons().length) {
      return result('composer-changed-before-paste', {}, 'Readiness');
    }
    const binary = atob(request.imageBase64);
    const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
    const transfer = new DataTransfer();
    transfer.items.add(new File([bytes], 'opengamemate-screen.png', { type: 'image/png' }));
    state.attachmentAttempted = true;
    composer.dispatchEvent(new ClipboardEvent('paste', {
      bubbles: true, cancelable: true, composed: true, clipboardData: transfer
    }));
    return result('paste-dispatched');
  }

  if (action === 'file-events') {
    if (state.invoked || !state.file?.isConnected || state.file.form !== form ||
        text(composer).trim() || stopButtons().length) {
      return result('file-context-changed', {}, 'Readiness');
    }
    // DOM.setFileInputFiles emits input/change itself. Do not emit duplicate uploads.
    return result('file-selected', { fileSelected: state.file.files?.length === 1 });
  }

  if (action === 'attachment' || action === 'attachment-timeout') {
    if (action === 'attachment-timeout') {
      // Observe only controls in this operation's form. Never return image URLs,
      // labels, HTML, composer text, or conversation content.
      const images = [...form.querySelectorAll('img')].filter(visible);
      const count = elements => Math.min(elements.length, 99);
      const schemeCount = scheme => count(images.filter(image =>
        image.getAttribute('src')?.startsWith(scheme)));
      const sends = sendButtons();
      return result(`attachment-timeout-p${Number(hasAttachment())}-u${Number(uploading())}` +
        `-i${count(images)}-b${schemeCount('blob:')}-d${schemeCount('data:')}` +
        `-h${schemeCount('https:')}-s${count(sends)}-e${Number(sends.length === 1 && enabled(sends[0]))}`,
        { fileSelected: Boolean(state.file?.files?.length === 1),
          previewDetected: hasAttachment(), uploading: uploading() }, 'Readiness');
    }
    return result('attachment-observed', {
      fileSelected: Boolean(state.file?.files?.length === 1),
      previewDetected: hasAttachment(), uploading: uploading()
    });
  }

  if (action === 'text') {
    if (state.invoked || text(composer).trim() || stopButtons().length) {
      return result('composer-changed-before-text', {}, 'Readiness');
    }
    if (state.requireImage && (!hasAttachment() || uploading())) {
      return result('attachment-not-ready', {}, 'Readiness');
    }
    if (composer instanceof HTMLTextAreaElement) {
      const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set;
      if (!setter) return result('native-value-setter-missing');
      setter.call(composer, request.message);
    } else {
      const paragraph = document.createElement('p');
      paragraph.textContent = request.message;
      composer.replaceChildren(paragraph);
    }
    composer.dispatchEvent(new InputEvent('input', {
      bubbles: true, inputType: 'insertText', data: request.message
    }));
    const inserted = text(composer) === request.message;
    if (inserted) state.expectedText = request.message;
    return result(inserted ? 'text-inserted' : 'text-not-inserted', { textInserted: inserted });
  }

  if (action === 'control' || action === 'invoke') {
    if (state.invoked) return result('already-invoked', {}, 'Invocation');
    if (state.voiceOpen !== voiceOpen()) {
      return result('voice-session-changed', {}, 'ButtonValidation');
    }
    if (state.expectedText === null || text(composer) !== state.expectedText) {
      return result('prepared-text-changed', {}, 'ButtonValidation');
    }
    if (state.requireImage && !hasAttachment()) {
      return result('attachment-missing-before-submit', {}, 'ButtonValidation');
    }
    if (request.expectedPageState && pageState() !== request.expectedPageState) {
      return result('page-state-changed-before-submit', {}, 'ButtonValidation');
    }
    const sends = sendButtons();
    const stops = stopButtons();
    const values = { sendButtonCount: sends.length, busyButtonCount: stops.length,
      sendDisabled: sends.length === 1 && !enabled(sends[0]), uploading: uploading() };
    if (quota() || platformError()) return result('platform-blocked', values, 'ButtonValidation');
    if (action === 'control') return result('ok', values);
    if (sends.length !== 1 || stops.length || !enabled(sends[0]) || uploading()) {
      return result('send-control-changed-before-invoke', values, 'Invocation');
    }
    // Latch before click: an exception or uncertain completion must never cause a second click.
    state.invoked = true;
    sends[0].click();
    return result('send-invoked', { invoked: true });
  }

  if (action === 'post') {
    if (!state.invoked) return result('send-not-invoked', {}, 'PostSubmitObservation');
    const composerCleared = !text(composer).trim();
    // Observe the scoped visible preview, not the existence of an upload input.
    const attachmentCleared = !hasAttachment();
    const consumed = composerCleared && (!state.requireImage || attachmentCleared);
    return result(consumed ? 'submission-observed' : 'submission-pending', {
      composerCleared, attachmentCleared, consumed
    }, 'PostSubmitObservation');
  }
  return result('unsupported-operation');
}
