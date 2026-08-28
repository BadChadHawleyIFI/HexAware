import { escapeHtml } from './htmlEscape';

/**
 * Converts a small, common subset of markdown (bold/italic, inline code, fenced code
 * blocks, bullet/numbered lists, headers, paragraphs) into safe HTML for the chat panel.
 * All text is HTML-escaped before any markup is introduced, so no raw HTML from the
 * model (or from HexQuery output it quotes) can ever reach the DOM unescaped.
 */
export function renderMarkdownToSafeHtml(markdown: string): string {
  const codeBlocks: string[] = [];
  const withPlaceholders = markdown
    .replace(/\r\n/g, '\n')
    .replace(/```[^\n]*\n?([\s\S]*?)```/g, (_match, code: string) => {
      const index = codeBlocks.length;
      codeBlocks.push(code.replace(/\n$/, ''));
      return `\u0000CODEBLOCK${index}\u0000`;
    });

  const lines = withPlaceholders.split('\n');
  const htmlParts: string[] = [];
  let listType: 'ul' | 'ol' | null = null;
  let paragraphBuffer: string[] = [];

  const flushParagraph = () => {
    if (paragraphBuffer.length) {
      htmlParts.push(`<p>${renderInline(paragraphBuffer.join(' '))}</p>`);
      paragraphBuffer = [];
    }
  };
  const closeList = () => {
    if (listType) {
      htmlParts.push(`</${listType}>`);
      listType = null;
    }
  };

  for (const rawLine of lines) {
    const line = rawLine.trimEnd();
    const trimmed = line.trim();

    const codeBlockMatch = /^\u0000CODEBLOCK(\d+)\u0000$/.exec(trimmed);
    if (codeBlockMatch) {
      flushParagraph();
      closeList();
      const code = codeBlocks[Number(codeBlockMatch[1])] ?? '';
      htmlParts.push(`<pre><code>${escapeHtml(code)}</code></pre>`);
      continue;
    }

    if (!trimmed) {
      flushParagraph();
      closeList();
      continue;
    }

    const headerMatch = /^(#{1,6})\s+(.*)$/.exec(trimmed);
    if (headerMatch) {
      flushParagraph();
      closeList();
      const tag = headerMatch[1].length <= 2 ? 'h4' : headerMatch[1].length <= 4 ? 'h5' : 'h6';
      htmlParts.push(`<${tag}>${renderInline(headerMatch[2])}</${tag}>`);
      continue;
    }

    const bulletMatch = /^[-*]\s+(.*)$/.exec(trimmed);
    if (bulletMatch) {
      flushParagraph();
      if (listType !== 'ul') {
        closeList();
        htmlParts.push('<ul>');
        listType = 'ul';
      }
      htmlParts.push(`<li>${renderInline(bulletMatch[1])}</li>`);
      continue;
    }

    const numberedMatch = /^\d+[.)]\s+(.*)$/.exec(trimmed);
    if (numberedMatch) {
      flushParagraph();
      if (listType !== 'ol') {
        closeList();
        htmlParts.push('<ol>');
        listType = 'ol';
      }
      htmlParts.push(`<li>${renderInline(numberedMatch[1])}</li>`);
      continue;
    }

    closeList();
    paragraphBuffer.push(trimmed);
  }

  flushParagraph();
  closeList();

  return htmlParts.join('');
}

function renderInline(text: string): string {
  return escapeHtml(text)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/__([^_]+)__/g, '<strong>$1</strong>')
    .replace(/\*([^*]+)\*/g, '<em>$1</em>')
    .replace(/(?<![A-Za-z0-9])_([^_]+)_(?![A-Za-z0-9])/g, '<em>$1</em>');
}
