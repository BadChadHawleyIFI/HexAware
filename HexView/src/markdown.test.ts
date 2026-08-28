import test from 'node:test';
import assert from 'node:assert/strict';
import { renderMarkdownToSafeHtml } from './markdown';

test('renderMarkdownToSafeHtml renders bold, italic, and inline code', () => {
  const html = renderMarkdownToSafeHtml('This is **bold**, this is *italic*, and this is `code`.');
  assert.match(html, /<strong>bold<\/strong>/);
  assert.match(html, /<em>italic<\/em>/);
  assert.match(html, /<code>code<\/code>/);
});

test('renderMarkdownToSafeHtml renders fenced code blocks verbatim', () => {
  const html = renderMarkdownToSafeHtml('```\nfunction foo() {}\n```');
  assert.match(html, /<pre><code>function foo\(\) \{\}<\/code><\/pre>/);
});

test('renderMarkdownToSafeHtml renders bullet and numbered lists', () => {
  const bulletHtml = renderMarkdownToSafeHtml('- one\n- two\n- three');
  assert.equal(bulletHtml, '<ul><li>one</li><li>two</li><li>three</li></ul>');

  const numberedHtml = renderMarkdownToSafeHtml('1. first\n2. second');
  assert.equal(numberedHtml, '<ol><li>first</li><li>second</li></ol>');
});

test('renderMarkdownToSafeHtml renders headers and plain paragraphs', () => {
  const html = renderMarkdownToSafeHtml('# Title\n\nSome plain text.');
  assert.match(html, /<h4>Title<\/h4>/);
  assert.match(html, /<p>Some plain text\.<\/p>/);
});

test('renderMarkdownToSafeHtml never emits unescaped HTML from the input', () => {
  const html = renderMarkdownToSafeHtml('Ignore <script>alert(1)</script> and "quotes" & ampersands.');
  assert.doesNotMatch(html, /<script>/);
  assert.match(html, /&lt;script&gt;/);
  assert.match(html, /&amp;/);
});

test('renderMarkdownToSafeHtml escapes HTML inside inline code and fenced blocks too', () => {
  const inlineHtml = renderMarkdownToSafeHtml('Use `<img src=x onerror=alert(1)>` carefully.');
  assert.doesNotMatch(inlineHtml, /<img/);
  assert.match(inlineHtml, /<code>&lt;img src=x onerror=alert\(1\)&gt;<\/code>/);

  const blockHtml = renderMarkdownToSafeHtml('```\n<img src=x onerror=alert(1)>\n```');
  assert.doesNotMatch(blockHtml, /<img/);
  assert.match(blockHtml, /&lt;img src=x onerror=alert\(1\)&gt;/);
});
