import axios from 'axios';

export function createHttpClient(options) {
  const endpoint = options.endpoint;
  if (!endpoint) throw new Error('HTTP模型需要配置options.endpoint');

  // 可选：超时与自定义请求头；支持Bearer令牌或自定义认证头
  const timeout = options.timeout || 30000;
  const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
  if (options.apiKey) {
    const prefix = options.authHeaderPrefix ?? 'Bearer ';
    headers['Authorization'] = `${prefix}${options.apiKey}`;
  }

  return {
    async review({ rulesText, diff, context }) {
      const payload = {
        rules: rulesText,
        diff,
        context
      };
      const res = await axios.post(endpoint, payload, { headers, timeout });
      // 约定返回 { review: "文本" }
      return res.data?.review || JSON.stringify(res.data);
    }
  };
}