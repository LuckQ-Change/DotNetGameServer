import axios from 'axios';

export function createHttpClient(options) {
  // 可选：超时与自定义请求头；支持Bearer令牌或自定义认证头
  const timeout = options.timeout || 30000;
  const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
  if (options.apiKey) {
    const prefix = options.authHeaderPrefix ?? 'Bearer ';
    headers['Authorization'] = `${prefix}${options.apiKey}`;
  }

  // 负载格式：默认使用项目自有的 {rules, diff, context}
  // 也支持 OpenAI 兼容的 chat/completions（方便直连第三方 OpenAI 风格接口，如混元）
  const payloadFormat = options.payloadFormat || 'native'; // 'native' | 'openai_chat'
  const model = options.model; // openai_chat 需要
  const temperature = options.temperature ?? 0.2;

  // 额外校验：当使用 openai_chat 负载时，endpoint 必须指向 /v1/chat/completions
  if (payloadFormat === 'openai_chat') {
    const isChatCompletions = /\/v1\/chat\/completions(\?.*)?$/i.test(endpoint);
    if (!isChatCompletions) {
      throw new Error(`HTTP(openai_chat) 需要 options.endpoint 指向 /v1/chat/completions；当前为: ${endpoint}`);
    }
  } else {
    // 友好提示：若使用 native 负载但 endpoint 指向 chat/completions，建议切换为 openai_chat
    if (/\/chat\/completions(\?.*)?$/i.test(endpoint)) {
      // 仅警告不抛错，避免影响已自建兼容层的场景
      console.warn('[AICodeReview] 检测到 payloadFormat=native 但 endpoint 指向 chat/completions，建议设置 payloadFormat:"openai_chat" 以避免协议不匹配。');
    }
  }

  function buildPayload({ rulesText, diff, context }) {
    if (payloadFormat === 'openai_chat') {
      if (!model) throw new Error('HTTP(openai_chat) 需要配置 options.model');
      const prompt = [
        '你是资深代码审查助手，请依据以下规则与提交差异给出问题点和优化方案。',
        '',
        '【规则】',
        rulesText,
        '',
        '【提交上下文】',
        `作者: ${context.authorName} <${context.authorEmail}>`,
        `提交信息: ${context.message}`,
        '',
        '【差异】',
        diff,
        '',
        '请输出简明的审查结论：',
        '- 发现的问题（列点）',
        '- 优化建议（列点，尽量贴近代码片段）',
        '- 如无问题也请说明理由'
      ].join('\n');
      return {
        model,
        messages: [
          { role: 'system', content: '你严格遵循给定规则进行代码审查，输出中文要点。' },
          { role: 'user', content: prompt }
        ],
        temperature
      };
    }
    // 默认 native 格式
    return {
      rules: rulesText,
      diff,
      context
    };
  }

  return {
    async review({ rulesText, diff, context }) {
      const payload = buildPayload({ rulesText, diff, context });
      const res = await axios.post(endpoint, payload, { headers, timeout });
      // 解析返回
      if (payloadFormat === 'openai_chat') {
        const data = res.data;
        return data?.choices?.[0]?.message?.content || data?.review || JSON.stringify(data);
      }
      // native：约定返回 { review: "文本" }
      return res.data?.review || JSON.stringify(res.data);
    }
  };
}