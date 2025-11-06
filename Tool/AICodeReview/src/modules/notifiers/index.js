import { sendToLark } from './lark.js';
import { sendToWeCom } from './wecom.js';
import { sendEmail } from './email.js';
import { formatSnippets } from '../utils/snippet-format.js';

function buildMessage(result, config) {
  const c = result.commit;
  const style = config.notifications?.reportStyle || 'full';
  const snippets = result.snippets || [];
  const snippetFormat = config.notifications?.snippetFormat || 'raw';
  const formatted = formatSnippets(snippets, snippetFormat);
  const snippetsText = formatted.map((s, i) => `片段${i + 1}:\n${s}`).join('\n\n');

  // 仅片段：不显示提交信息
  if (style === 'snippets_only') {
    const plain = formatted.join('\n\n').trim();
    return plain || '（未提取到片段）';
  }

  // 仅问题：只显示审查建议，不含提交信息与片段
  if (style === 'issues_only') {
    const issues = (result.reviewText || '').trim();
    return issues || '（未检出问题）';
  }

  // 问题 + 片段：不显示提交信息，仅展示问题与片段
  if (style === 'issues_with_snippets') {
    const issues = (result.reviewText || '').trim();
    const parts = [];
    if (issues) parts.push(`审查建议:\n${issues}`);
    if (snippetsText) parts.push(snippetsText);
    return parts.join('\n\n').trim() || '（无可展示内容）';
  }

  // 完整：包含提交信息、片段与建议
  const header = `AI代码审核结果\n仓库提交: ${c.hash}\n作者: ${c.authorName} <${c.authorEmail}>\n时间: ${c.date}\n说明: ${c.message}`;
  const body = `${header}\n\n${snippetsText ? snippetsText + '\n\n' : ''}审查建议:\n${result.reviewText}`;
  return body;
}

export async function notifyResults({ config, results }) {
  for (const r of results) {
    // 跳过无内容的结果：当未提取到片段且用于审查的diff为空
    if ((!r.snippets || r.snippets.length === 0) && r.usedDiffEmpty) {
      console.log('[notifiers] 跳过发送：无片段且diff为空', r.commit?.hash);
      continue;
    }

    const message = buildMessage(r, config);
    const authorEmail = r.commit.authorEmail;
    const larkOpenId = config.mention_map?.email_to_lark_open_id?.[authorEmail];
    const wecomUserId = config.mention_map?.email_to_wecom_userid?.[authorEmail];

    const larkCfg = config.notifications?.lark;
    if (larkCfg?.enabled) {
      if (larkCfg.webhook) {
        await sendToLark({ webhook: larkCfg.webhook, message, openId: larkOpenId, authorName: r.commit.authorName });
      } else {
        console.warn('[notifiers] 已启用飞书但缺少webhook，已跳过发送');
      }
    }

    const wecomCfg = config.notifications?.wecom;
    if (wecomCfg?.enabled) {
      if (wecomCfg.webhook) {
        await sendToWeCom({ webhook: wecomCfg.webhook, message, userId: wecomUserId, authorName: r.commit.authorName });
      } else {
        console.warn('[notifiers] 已启用企业微信但缺少webhook，已跳过发送');
      }
    }

    const emailCfg = config.notifications?.email;
    if (emailCfg?.enabled) {
      if (emailCfg.smtp?.host && emailCfg.from) {
        const style = config.notifications?.reportStyle || 'full';
        const subject = ['snippets_only','issues_only','issues_with_snippets'].includes(style) ? 'AI代码审核' : `AI代码审核: ${r.commit.message}`;
        await sendEmail({ smtp: emailCfg.smtp, from: emailCfg.from, to: authorEmail, subject, text: message });
      } else {
        console.warn('[notifiers] 已启用邮件但缺少SMTP或from，已跳过发送');
      }
    }
  }
}