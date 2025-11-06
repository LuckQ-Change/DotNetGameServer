import { sendToLark } from './lark.js';
import { sendToWeCom } from './wecom.js';
import { sendEmail } from './email.js';

function buildMessage(result, config) {
  const c = result.commit;
  const header = `AI代码审核结果\n仓库提交: ${c.hash}\n作者: ${c.authorName} <${c.authorEmail}>\n时间: ${c.date}\n说明: ${c.message}`;
  const snippets = (result.snippets || []).map((s, i) => `片段${i + 1}:\n${s}`).join('\n\n');
  const body = `${header}\n\n${snippets ? snippets + '\n\n' : ''}审查建议:\n${result.reviewText}`;
  return body;
}

export async function notifyResults({ config, results }) {
  for (const r of results) {
    const message = buildMessage(r, config);
    const authorEmail = r.commit.authorEmail;
    const larkOpenId = config.mention_map?.email_to_lark_open_id?.[authorEmail];
    const wecomUserId = config.mention_map?.email_to_wecom_userid?.[authorEmail];

    if (config.notifications?.lark?.enabled) {
      await sendToLark({ webhook: config.notifications.lark.webhook, message, openId: larkOpenId, authorName: r.commit.authorName });
    }
    if (config.notifications?.wecom?.enabled) {
      await sendToWeCom({ webhook: config.notifications.wecom.webhook, message, userId: wecomUserId, authorName: r.commit.authorName });
    }
    if (config.notifications?.email?.enabled) {
      await sendEmail({ smtp: config.notifications.email.smtp, from: config.notifications.email.from, to: authorEmail, subject: `AI代码审核: ${r.commit.message}`, text: message });
    }
  }
}