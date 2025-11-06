import OpenAI from 'openai';

export function createOpenAIClient(options) {
  // 支持自定义 baseURL，以兼容 OpenAI 风格的第三方服务（如企业私有或云厂商）
  const client = new OpenAI({ apiKey: options.apiKey, baseURL: options.baseURL });
  const model = options.model || 'gpt-4o-mini';
  return {
    async review({ rulesText, diff, context }) {
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
        '语法和样式：查找语法错误和与约定的偏差',
        '性能优化：提出更改建议以提高效率',
        '安全实践：检查漏洞和硬编码密钥（掩盖一半信息）',
        '错误处理：识别未处理的异常或错误',
        '代码质量：查找代码异味、不必要的复杂性或冗余代码',
        'Bug 检测：查找潜在 Bug 或逻辑错误',
      ].join('\n');

      const res = await client.chat.completions.create({
        model,
        messages: [
          { role: 'system', content: '你严格遵循给定规则进行代码审查，输出中文要点。' },
          { role: 'user', content: prompt }
        ],
        temperature: 0.2
      });
      return res.choices?.[0]?.message?.content || '';
    }
  };
}