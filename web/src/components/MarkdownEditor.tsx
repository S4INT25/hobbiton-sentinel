import { useRef, useState, useCallback, type ReactNode } from 'react';
import { Markdown } from './ui';

type Mode = 'write' | 'split' | 'preview';

/** Applies a transform to the textarea selection and restores the caret. */
function useSurface(
  ref: React.RefObject<HTMLTextAreaElement | null>,
  value: string,
  onChange: (v: string) => void,
) {
  return useCallback(
    (fn: (sel: string, before: string, after: string) => { text: string; caret: [number, number] }) => {
      const el = ref.current;
      if (!el) return;
      const { selectionStart: s, selectionEnd: e } = el;
      const { text, caret } = fn(value.slice(s, e), value.slice(0, s), value.slice(e));
      onChange(text);
      requestAnimationFrame(() => {
        el.focus();
        el.setSelectionRange(caret[0], caret[1]);
      });
    },
    [ref, value, onChange],
  );
}

/** Wrap selection in a marker, e.g. **bold**. Empty selection leaves the caret inside. */
const wrap = (mark: string, placeholder = '') =>
  (sel: string, before: string, after: string) => {
    const body = sel || placeholder;
    return {
      text: `${before}${mark}${body}${mark}${after}`,
      caret: [before.length + mark.length, before.length + mark.length + body.length] as [number, number],
    };
  };

/** Prefix every selected line, e.g. "## " or "- ". */
const prefixLines = (mark: string, placeholder = '') =>
  (sel: string, before: string, after: string) => {
    // extend backwards to the start of the current line so the prefix lands correctly
    const lineStart = before.lastIndexOf('\n') + 1;
    const head = before.slice(0, lineStart);
    const partial = before.slice(lineStart);
    const body = partial + (sel || placeholder);
    const prefixed = body
      .split('\n')
      .map((l, i) => (mark === '1. ' ? `${i + 1}. ${l}` : `${mark}${l}`))
      .join('\n');
    return {
      text: `${head}${prefixed}${after}`,
      caret: [head.length + prefixed.length, head.length + prefixed.length] as [number, number],
    };
  };

/** Insert a block on its own lines, e.g. a fenced code block or a table. */
const block = (make: (sel: string) => string, caretOffset?: number) =>
  (sel: string, before: string, after: string) => {
    const lead = before && !before.endsWith('\n') ? '\n' : '';
    const tail = after && !after.startsWith('\n') ? '\n' : '';
    const body = make(sel);
    const start = before.length + lead.length;
    return {
      text: `${before}${lead}${body}${tail}${after}`,
      caret: [start + (caretOffset ?? body.length), start + (caretOffset ?? body.length)] as [number, number],
    };
  };

const linkAction = (sel: string, before: string, after: string) => {
  const label = sel || 'text';
  const md = `[${label}](url)`;
  const urlAt = before.length + label.length + 3;
  return { text: `${before}${md}${after}`, caret: [urlAt, urlAt + 3] as [number, number] };
};

const imageAction = (sel: string, before: string, after: string) => {
  const alt = sel || 'alt text';
  const md = `![${alt}](https://)`;
  const urlAt = before.length + alt.length + 4;
  return { text: `${before}${md}${after}`, caret: [urlAt + 8, urlAt + 8] as [number, number] };
};

const TABLE = '| Column | Column |\n|---|---|\n|  |  |';

export function MarkdownEditor({
  value,
  onChange,
  rows = 14,
  placeholder,
  className,
}: {
  value: string;
  onChange: (v: string) => void;
  rows?: number;
  placeholder?: string;
  className?: string;
}) {
  const ref = useRef<HTMLTextAreaElement>(null);
  const apply = useSurface(ref, value, onChange);
  const [mode, setMode] = useState<Mode>('write');

  const onKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    const meta = e.metaKey || e.ctrlKey;
    if (meta && e.key === 'b') { e.preventDefault(); apply(wrap('**', 'bold')); }
    else if (meta && e.key === 'i') { e.preventDefault(); apply(wrap('*', 'italic')); }
    else if (meta && e.key === 'k') { e.preventDefault(); apply(linkAction); }
    else if (e.key === 'Tab') {
      // keep Tab inside the editor — markdown nesting needs it more than focus traversal does
      e.preventDefault();
      apply((sel, before, after) => ({
        text: `${before}  ${sel}${after}`,
        caret: [before.length + 2, before.length + 2 + sel.length],
      }));
    }
  };

  const tools: ({ label: ReactNode; title: string; run: () => void } | 'sep')[] = [
    { label: 'H1', title: 'Heading 1', run: () => apply(prefixLines('# ', 'Heading')) },
    { label: 'H2', title: 'Heading 2', run: () => apply(prefixLines('## ', 'Heading')) },
    { label: 'H3', title: 'Heading 3', run: () => apply(prefixLines('### ', 'Heading')) },
    'sep',
    { label: <span className="font-bold">B</span>, title: 'Bold  ⌘B', run: () => apply(wrap('**', 'bold')) },
    { label: <span className="italic">I</span>, title: 'Italic  ⌘I', run: () => apply(wrap('*', 'italic')) },
    { label: <span className="font-mono">{'<>'}</span>, title: 'Inline code', run: () => apply(wrap('`', 'code')) },
    'sep',
    { label: '• List', title: 'Bulleted list', run: () => apply(prefixLines('- ', 'item')) },
    { label: '1. List', title: 'Numbered list', run: () => apply(prefixLines('1. ', 'item')) },
    { label: '❝', title: 'Quote', run: () => apply(prefixLines('> ', 'quote')) },
    'sep',
    { label: 'Link', title: 'Link  ⌘K', run: () => apply(linkAction) },
    { label: 'Image', title: 'Image', run: () => apply(imageAction) },
    { label: 'Table', title: 'Table', run: () => apply(block(() => TABLE, 2)) },
    {
      label: 'Code',
      title: 'Code block',
      run: () => apply(block((sel) => '```sql\n' + (sel || '') + '\n```', 7)),
    },
  ];

  const editor = (
    <textarea
      ref={ref}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      onKeyDown={onKeyDown}
      rows={rows}
      spellCheck={false}
      placeholder={placeholder}
      className="w-full h-full min-h-0 resize-y bg-gray-950/60 px-3 py-2.5 font-mono text-[13px] leading-relaxed text-gray-200 placeholder-gray-600 focus:outline-none"
    />
  );

  const preview = (
    <div className="h-full overflow-y-auto bg-gray-950/30 px-3 py-2.5">
      {value.trim() ? (
        <Markdown text={value} />
      ) : (
        <p className="text-xs text-gray-600 italic">Nothing to preview yet.</p>
      )}
    </div>
  );

  return (
    <div className={`rounded-md border border-gray-700/80 overflow-hidden focus-within:border-emerald-400/60 focus-within:ring-2 focus-within:ring-emerald-500/15 transition-all ${className ?? ''}`}>
      <div className="flex items-center gap-0.5 border-b border-gray-800 bg-gray-900/60 px-1.5 py-1 overflow-x-auto">
        {tools.map((t, i) =>
          t === 'sep' ? (
            <span key={i} className="mx-1 h-4 w-px shrink-0 bg-gray-800" />
          ) : (
            <button
              key={i}
              type="button"
              title={t.title}
              onClick={t.run}
              className="shrink-0 rounded px-1.5 py-1 text-[11px] text-gray-400 hover:bg-gray-800 hover:text-white transition-colors"
            >
              {t.label}
            </button>
          ),
        )}
        <div className="ml-auto flex shrink-0 items-center gap-0.5 pl-2">
          {(['write', 'split', 'preview'] as Mode[]).map((m) => (
            <button
              key={m}
              type="button"
              onClick={() => setMode(m)}
              className={`rounded px-2 py-1 text-[10px] font-medium uppercase tracking-wide transition-colors ${
                mode === m ? 'bg-gray-800 text-white' : 'text-gray-500 hover:text-gray-300'
              }`}
            >
              {m}
            </button>
          ))}
        </div>
      </div>

      {mode === 'split' ? (
        <div className="grid grid-cols-2 divide-x divide-gray-800" style={{ height: `${rows * 1.6 + 1.5}rem` }}>
          {editor}
          {preview}
        </div>
      ) : mode === 'preview' ? (
        <div style={{ height: `${rows * 1.6 + 1.5}rem` }}>{preview}</div>
      ) : (
        editor
      )}
    </div>
  );
}

/** Clean, wide reading view for markdown content. */
export function MarkdownReader({ text, className }: { text: string | null | undefined; className?: string }) {
  if (!text?.trim()) {
    return <p className="text-xs text-gray-600 italic">No content.</p>;
  }
  return <Markdown text={text} className={`md-reading ${className ?? ''}`} />;
}
