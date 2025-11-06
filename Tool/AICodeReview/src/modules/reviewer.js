import { extractSnippets } from './utils/snippets.js';
import { filterDiffByPaths } from './utils/diff-filter.js';

export async function reviewCommits({ config, rules, model, commits }) {
  const results = [];
  const maxSnippets = config.review?.maxSnippetsPerCommit ?? 2;
  const maxLines = config.review?.maxLinesPerSnippet ?? 20;
  const include = config.review?.include ?? [];
  const exclude = config.review?.exclude ?? [];

  for (const c of commits) {
    const filteredDiff = filterDiffByPaths(c.diff, include, exclude);
    const snippets = extractSnippets(filteredDiff, maxSnippets, maxLines);
    // 为避免请求体过大（导致HTTP 413等），仅将提取的片段传给模型，而非整份diff
    const compactDiff = (snippets && snippets.length) ? snippets.join('\n\n') : (filteredDiff || '');
    const reviewText = await model.review({
      rulesText: rules.text,
      diff: compactDiff,
      context: {
        message: c.message,
        authorName: c.authorName,
        authorEmail: c.authorEmail,
        hash: c.hash,
        date: c.date
      }
    });

    results.push({
      commit: c,
      reviewText,
      snippets
    });
  }
  return results;
}