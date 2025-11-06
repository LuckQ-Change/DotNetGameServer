import axios from 'axios';

export async function sendToLark({ webhook, message, openId, authorName }) {
  if (!webhook) throw new Error('缺少飞书webhook');
  let text = message;
  // 简单@作者（若映射存在，可使用开放ID增强）
  if (openId) {
    // 飞书文本消息支持at标签（根据机器人能力，具体细节可能因群设置而异）。
    text = `@${authorName}\n${message}`;
  } else {
    text = `@${authorName}\n${message}`;
  }
  const payload = {
    msg_type: 'text',
    content: {
      text
    }
  };
  await axios.post(webhook, payload);
}