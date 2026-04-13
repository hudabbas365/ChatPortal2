// aiStream.js — Shared SSE / streaming helper for AI features
// Used by: dashboard-chat-panel.js, propertiesPanel.js
(function (global) {
    'use strict';

    /**
     * Read a streaming (SSE) or non-streaming AI response.
     *
     * @param {Response}  response  – fetch() Response object
     * @param {Function}  onToken   – optional callback(textChunk) called for each token as it arrives
     * @returns {Promise<{fullText: string, httpStatus: number}>}
     */
    async function readSseText(response, onToken) {
        const httpStatus = response.status;
        const contentType = response.headers.get('content-type') || '';

        // ── Non-streaming fallback ────────────────────────────────────────
        if (!contentType.includes('text/event-stream')) {
            let body = '';
            try {
                body = await response.text();
                // Try to parse as JSON and extract a text field
                const parsed = JSON.parse(body);
                const text = parsed.text || parsed.message || parsed.content || body;
                if (typeof onToken === 'function') onToken(text);
                return { fullText: text, httpStatus };
            } catch {
                if (typeof onToken === 'function') onToken(body);
                return { fullText: body, httpStatus };
            }
        }

        // ── SSE streaming path ────────────────────────────────────────────
        const reader  = response.body.getReader();
        const decoder = new TextDecoder();
        let pending   = '';   // carries incomplete line fragments across chunks
        let fullText  = '';
        let done      = false;

        while (!done) {
            const { done: streamDone, value } = await reader.read();
            if (streamDone) break;

            // Append new chunk to any leftover fragment from previous read
            pending += decoder.decode(value, { stream: true });

            // Split on newline; last element may be an incomplete line
            const lines = pending.split('\n');
            // Keep the last (potentially incomplete) line for the next iteration
            pending = lines.pop();

            for (const line of lines) {
                const trimmed = line.trimEnd();
                if (!trimmed.startsWith('data: ')) continue;

                const data = trimmed.slice(6);

                if (data === '[DONE]') {
                    done = true;
                    break;
                }

                try {
                    const parsed = JSON.parse(data);
                    // Supports common AI SSE payload shapes:
                    //   { "text": "..." }    – ChatPortal / custom backend
                    //   { "delta": "..." }   – some streaming APIs
                    //   { "content": "..." } – OpenAI-compatible
                    const chunk  = parsed.text || parsed.delta || parsed.content || '';
                    if (chunk) {
                        fullText += chunk;
                        if (typeof onToken === 'function') onToken(chunk);
                    }
                } catch {
                    // Skip malformed JSON lines silently
                }
            }
        }

        // Flush any remainder that didn't end with a newline
        if (!done && pending.startsWith('data: ')) {
            const data = pending.slice(6).trimEnd();
            if (data && data !== '[DONE]') {
                try {
                    const parsed = JSON.parse(data);
                    const chunk  = parsed.text || parsed.delta || parsed.content || '';
                    if (chunk) {
                        fullText += chunk;
                        if (typeof onToken === 'function') onToken(chunk);
                    }
                } catch {}
            }
        }

        return { fullText, httpStatus };
    }

    // Expose on window so other scripts can use it
    global.aiStream = { readSseText };

}(window));
