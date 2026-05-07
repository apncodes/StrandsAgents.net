const messages = document.getElementById('messages');
const input    = document.getElementById('input');
const sendBtn  = document.getElementById('send-btn');
const welcome  = document.getElementById('welcome');

// Stable session ID for this browser tab — persists conversation history
const sessionId = crypto.randomUUID();

let streaming = false;
let currentToolPill = null;
let currentAgentBubble = null;
let currentAgentText = '';

function setInput(text) { input.value = text; input.focus(); }

function handleKey(e) {
  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); }
}

function scrollBottom() { messages.scrollTop = messages.scrollHeight; }

function addRow(cls) {
  const row = document.createElement('div');
  row.className = `row ${cls}`;
  messages.appendChild(row);
  return row;
}

function addUserBubble(text) {
  if (welcome) welcome.style.display = 'none';
  const row = addRow('user');
  const b = document.createElement('div');
  b.className = 'bubble';
  b.textContent = text;
  row.appendChild(b);
  scrollBottom();
}

function addThinking() {
  const row = addRow('agent');
  const av = document.createElement('div'); av.className = 'avatar'; av.textContent = '🤖';
  const b  = document.createElement('div'); b.className = 'bubble thinking';
  b.innerHTML = '<div class="dot"></div><div class="dot"></div><div class="dot"></div>';
  row.appendChild(av); row.appendChild(b);
  scrollBottom();
  return row;
}

function addToolPill(name) {
  const row = addRow('tool');
  const pill = document.createElement('div'); pill.className = 'tool-pill';
  pill.innerHTML = `<span>🔧</span><span class="name">${escHtml(name)}</span><span class="status running">searching…</span>`;
  row.appendChild(pill);
  scrollBottom();
  return pill;
}

function addAgentBubble() {
  const row = addRow('agent');
  const av = document.createElement('div'); av.className = 'avatar'; av.textContent = '🤖';
  const b  = document.createElement('div'); b.className = 'bubble';
  b.innerHTML = '<span class="cursor">▌</span>';
  row.appendChild(av); row.appendChild(b);
  scrollBottom();
  return b;
}

function escHtml(s) {
  return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

function renderText(raw) {
  let s = escHtml(raw);
  s = s.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
  s = s.replace(/\*(.+?)\*/g, '<em>$1</em>');
  s = s.replace(/\n/g, '<br>');
  return s;
}

async function sendMessage() {
  const text = input.value.trim();
  if (!text || streaming) return;

  input.value = '';
  streaming = true;
  sendBtn.disabled = true;
  input.disabled = true;

  addUserBubble(text);

  let thinkingRow = addThinking();
  currentAgentBubble = null;
  currentAgentText = '';
  currentToolPill = null;

  try {
    const resp = await fetch('/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: text, sessionId })
    });

    const reader = resp.body.getReader();
    const decoder = new TextDecoder();
    let buf = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buf += decoder.decode(value, { stream: true });

      const parts = buf.split('\n\n');
      buf = parts.pop(); // keep incomplete chunk

      for (const part of parts) {
        const lines = part.trim().split('\n');
        let evtType = 'message', evtData = '';
        for (const line of lines) {
          if (line.startsWith('event: ')) evtType = line.slice(7);
          if (line.startsWith('data: '))  evtData = JSON.parse(line.slice(6));
        }

        if (evtType === 'delta') {
          // Remove thinking indicator on first token
          if (thinkingRow) { thinkingRow.remove(); thinkingRow = null; }
          if (!currentAgentBubble) { currentAgentBubble = addAgentBubble(); }
          currentAgentText += evtData;
          currentAgentBubble.innerHTML = renderText(currentAgentText) + '<span class="cursor">▌</span>';
          scrollBottom();
        }

        else if (evtType === 'tool_start') {
          if (thinkingRow) { thinkingRow.remove(); thinkingRow = null; }
          // Finalise any in-progress agent bubble before showing tool pill
          if (currentAgentBubble) {
            currentAgentBubble.innerHTML = renderText(currentAgentText);
            currentAgentBubble = null;
            currentAgentText = '';
          }
          currentToolPill = addToolPill(evtData);
        }

        else if (evtType === 'tool_done') {
          if (currentToolPill) {
            currentToolPill.querySelector('.status').className = 'status done';
            currentToolPill.querySelector('.status').textContent = 'done';
            currentToolPill = null;
          }
          // New agent bubble for follow-up text
          currentAgentBubble = null;
          currentAgentText = '';
        }

        else if (evtType === 'done') {
          if (thinkingRow) { thinkingRow.remove(); thinkingRow = null; }
          if (currentAgentBubble) {
            currentAgentBubble.innerHTML = renderText(currentAgentText);
            currentAgentBubble = null;
          }
        }
      }
    }
  } catch (err) {
    if (thinkingRow) thinkingRow.remove();
    const row = addRow('agent');
    const av = document.createElement('div'); av.className = 'avatar'; av.textContent = '🤖';
    const b  = document.createElement('div'); b.className = 'bubble';
    b.textContent = `Error: ${err.message}`;
    row.appendChild(av); row.appendChild(b);
  } finally {
    streaming = false;
    sendBtn.disabled = false;
    input.disabled = false;
    input.focus();
    scrollBottom();
  }
}
