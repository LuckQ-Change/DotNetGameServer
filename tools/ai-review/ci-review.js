// 适用于 CI 的 AI 代码审核命令行工具
// - 在 CI 工作空间采集 git diff
// - 根据可自定义的审核规范构建提示词
// - 调用兼容 OpenAI 的 Chat Completions 接口
// - 输出 JSON 审核报告，并通过飞书、企业微信、邮箱推送摘要通知

// 加载 .env 并允许覆盖已有环境变量，避免历史会话变量干扰
require('dotenv').config({ override: true });
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');
const axios = require('axios');
const nodemailer = require('nodemailer');

const AI_BASE_URL = (process.env.AI_BASE_URL || 'https://api.openai.com/v1').replace(/\/$/, '');
const AI_ENDPOINT_PATH = (() => {
  const p = process.env.AI_ENDPOINT_PATH || '/chat/completions';
  return p.startsWith('/') ? p : `/${p}`;
})();
const AI_API_KEY = process.env.AI_API_KEY || '';
const AI_MODEL = process.env.AI_MODEL || 'gpt-4o-mini';
const AI_TEMPERATURE = Number(process.env.AI_TEMPERATURE || 0);
const AI_DRY_RUN = String(process.env.AI_DRY_RUN || 'false').toLowerCase() === 'true';

const DIFF_BASE = process.env.DIFF_BASE || '';
const DIFF_TARGET = process.env.DIFF_TARGET || '';

const FEISHU_WEBHOOK_URL = process.env.FEISHU_WEBHOOK_URL || '';
const WECOM_WEBHOOK_URL = process.env.WECOM_WEBHOOK_URL || '';

const SMTP_HOST = process.env.SMTP_HOST || '';
const SMTP_PORT = Number(process.env.SMTP_PORT || 465);
const SMTP_SECURE = String(process.env.SMTP_SECURE || 'true').toLowerCase() === 'true';
const SMTP_USER = process.env.SMTP_USER || '';
const SMTP_PASS = process.env.SMTP_PASS || '';
const SMTP_FROM = process.env.SMTP_FROM || '';
const SMTP_TO = process.env.SMTP_TO || '';

const REVIEWS_DIR = path.join(process.cwd(), 'reviews');
const CONFIG_DIR = path.join(process.cwd(), 'config');

// 确保输出目录与配置目录存在
function ensureDirs() {
  if (!fs.existsSync(REVIEWS_DIR)) fs.mkdirSync(REVIEWS_DIR, { recursive: true });
  if (!fs.existsSync(CONFIG_DIR)) fs.mkdirSync(CONFIG_DIR, { recursive: true });
}

// 加载审核规则：优先使用仓库提供的 rules.json，其次回退到示例文件
function loadRules() {
  const candidate = path.join(CONFIG_DIR, 'rules.json');
  const fallback = path.join(CONFIG_DIR, 'rules.example.json');
  if (fs.existsSync(candidate)) {
    return JSON.parse(fs.readFileSync(candidate, 'utf8'));
  }
  if (fs.existsSync(fallback)) {
    return JSON.parse(fs.readFileSync(fallback, 'utf8'));
  }
  return { name: '默认审核规范', categories: [
    { key: 'security', description: '安全风险' },
    { key: 'secrets', description: '硬编码凭证' },
    { key: 'quality', description: '代码质量' },
    { key: 'performance', description: '性能问题' },
    { key: 'testing', description: '测试与覆盖率' },
    { key: 'correctness', description: '正确性与边界' }
  ] };
}

// 执行 git 命令并返回标准输出文本
function execGit(args, opts = {}) {
  return execSync(['git'].concat(args).join(' '), { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], ...opts }).trim();
}

// 采集改动：确定比较范围并生成统一 diff（最小上下文）
function collectDiff() {
  let base = DIFF_BASE;
  let target = DIFF_TARGET;
  try {
    if (!target) target = execGit(['rev-parse', 'HEAD']);
  } catch (e) {
    throw new Error('不是 git 仓库或缺少 HEAD。请确认 CI 已检出代码。');
  }
  if (!base) {
    // 默认使用上一个提交作为基线
    try { base = execGit(['rev-parse', 'HEAD~1']); } catch (_) {}
  }
  // 若无 base，则直接与目标提交比较工作区
  const range = base ? `${base}..${target}` : target;
  let diff = '';
  try {
    // 生成统一 diff（-U0 最小上下文）
    diff = execGit(['diff', '--unified=0', range]);
  } catch (e) {
    // 初次运行的兜底：仅输出文件变更清单
    diff = execGit(['diff', '--name-status', range]);
  }
  return { base, target, diff };
}

// 构建提示词：包含仓库上下文、审核规范与代码改动
function buildPrompt(rules, diff, context) {
  const system = `你是一名资深代码审查助手。根据给定的审核规范与代码改动，生成结构化、可执行的审查建议。输出重点包括：风险等级、问题类别、具体位置与建议修复方案。`;
  const user = [
    `仓库: ${context.repoName}`,
    `提交: ${context.target}`,
    `基线: ${context.base || 'N/A'}`,
    `审核规范:`,
    JSON.stringify(rules, null, 2),
    `代码改动 (git diff -U0):`,
    diff || '(无改动)'
  ].join('\n');
  return [
    { role: 'system', content: system },
    { role: 'user', content: user }
  ];
}

// 调用兼容 OpenAI 的 Chat Completions 接口，返回文本内容
async function callChatCompletions(messages) {
  if (AI_DRY_RUN) {
    const userMsg = messages.find(m => m.role === 'user');
    const diffLen = (userMsg && userMsg.content && userMsg.content.length) || 0;
    return `【演示模式】未调用外部AI接口。根据当前配置生成占位审核结果。\n- 提示词长度: ${diffLen}\n- 请在 CI 中配置 AI_BASE_URL / AI_API_KEY / AI_MODEL 以启用真实审查。`;
  }
  const url = `${AI_BASE_URL}${AI_ENDPOINT_PATH}`;
  const headers = { 'Content-Type': 'application/json' };
  if (AI_API_KEY) headers['Authorization'] = `Bearer ${AI_API_KEY}`;
  const body = { model: AI_MODEL, messages, temperature: AI_TEMPERATURE };
  try {
    const resp = await axios.post(url, body, { headers });
    const choice = resp.data && resp.data.choices && resp.data.choices[0];
    const content = choice && choice.message && choice.message.content || '';
    return content;
  } catch (err) {
    if (err.response) {
      const status = err.response.status;
      let dataText = '';
      try { dataText = JSON.stringify(err.response.data); } catch (_) { dataText = String(err.response.data); }
      throw new Error(`AI接口错误 HTTP ${status}: ${dataText}`);
    }
    throw new Error(`网络错误: ${err.message}`);
  }
}

// 生成用于通知的 Markdown 文本，适当截断超长内容
function summarizeForMarkdown(context, aiText) {
  const head = `【AI 代码审核】仓库: ${context.repoName} 提交: ${context.target}`;
  const body = aiText.length > 3500 ? aiText.slice(0, 3500) + '\n...(已截断)' : aiText;
  return `${head}\n\n${body}`;
}

// 发送飞书（自定义机器人）通知
async function sendFeishu(text) {
  if (!FEISHU_WEBHOOK_URL) return;
  const payload = { msg_type: 'text', content: { text } };
  try { await axios.post(FEISHU_WEBHOOK_URL, payload); } catch (e) { console.error('Feishu notify failed:', e.message); }
}

// 发送企业微信（自定义机器人）通知
async function sendWeCom(text) {
  if (!WECOM_WEBHOOK_URL) return;
  const payload = { msgtype: 'markdown', markdown: { content: text.replace(/</g, '&lt;') } };
  try { await axios.post(WECOM_WEBHOOK_URL, payload); } catch (e) { console.error('WeCom notify failed:', e.message); }
}

// 通过 SMTP 发送邮件通知（纯文本）
async function sendEmail(subject, text) {
  if (!SMTP_HOST || !SMTP_TO || !SMTP_FROM) return;
  const transporter = nodemailer.createTransport({
    host: SMTP_HOST,
    port: SMTP_PORT,
    secure: SMTP_SECURE,
    auth: SMTP_USER ? { user: SMTP_USER, pass: SMTP_PASS } : undefined
  });
  try {
    await transporter.sendMail({ from: SMTP_FROM, to: SMTP_TO, subject, text });
  } catch (e) { console.error('Email notify failed:', e.message); }
}

// 解析仓库名，优先从 remote.origin.url 提取（org/repo）
function resolveRepoName() {
  try {
    const url = execGit(['config', '--get', 'remote.origin.url']);
    // 例：git@github.com:org/repo.git 或 https://github.com/org/repo.git
    const m = url.match(/[:/]([^/]+)\/([^/]+?)(\.git)?$/);
    if (m) return `${m[1]}/${m[2].replace(/\.git$/, '')}`;
  } catch (_) {}
  return 'local-repo';
}

// 主流程：收集 diff → 生成提示词并调用模型 → 保存结果 → 推送通知
async function run() {
  ensureDirs();
  const rules = loadRules();
  const { base, target, diff } = collectDiff();
  const context = { repoName: resolveRepoName(), base, target };

  const messages = buildPrompt(rules, diff, context);
  const aiText = await callChatCompletions(messages);

  const output = {
    repo: context.repoName,
    base,
    target,
    rules,
    created_at: new Date().toISOString(),
    ai_review: aiText
  };
  const fileName = `review_${(target || 'unknown').slice(0, 12)}_${Date.now()}.json`;
  const filePath = path.join(REVIEWS_DIR, fileName);
  fs.writeFileSync(filePath, JSON.stringify(output, null, 2), 'utf8');
  console.log('Review saved:', filePath);

  const summary = summarizeForMarkdown(context, aiText);
  await sendFeishu(summary);
  await sendWeCom(summary);
  await sendEmail(`AI代码审核 ${context.repoName}@${(target||'').slice(0,7)}`, summary);
}

// 异常兜底：确保 CI 任务正确标记失败状态
run().catch(err => {
  console.error('CI review failed:', err.message);
  process.exitCode = 1;
});