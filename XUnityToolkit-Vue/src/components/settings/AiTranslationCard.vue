<script setup lang="ts">
import { ref, computed } from 'vue'
import {
  NSelect,
  NInput,
  NButton,
  NIcon,
  NSwitch,
  NTag,
  NPopconfirm,
  NCollapse,
  NCollapseItem,
  NSlider,
  NAlert,
  useMessage,
} from 'naive-ui'
import {
  SmartToyOutlined,
  RestoreOutlined,
  ScienceOutlined,
  AddOutlined,
  DeleteOutlined,
  SearchOutlined,
} from '@vicons/material'
import type {
  AiTranslationSettings,
  ApiEndpointConfig,
  LlmApiFormat,
  LlmProvider,
  LlmReasoningEffort,
} from '@/api/types'
import { translateApi } from '@/api/games'
import { DEFAULT_SYSTEM_PROMPT } from '@/constants/prompts'

const props = withDefaults(defineProps<{
  modelValue: AiTranslationSettings
  embedded?: boolean
}>(), {
  embedded: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: AiTranslationSettings]
}>()

const message = useMessage()
const testing = ref(false)
const fetchingModels = ref<Record<string, boolean>>({})
const AUTOMATIC_AGENT_ENDPOINT = '__automatic__'

function update(patch: Partial<AiTranslationSettings>) {
  emit('update:modelValue', { ...props.modelValue, ...patch })
}

function updateEndpoint(index: number, patch: Partial<ApiEndpointConfig>) {
  const endpoints: ApiEndpointConfig[] = [...props.modelValue.endpoints]
  endpoints[index] = Object.assign({}, endpoints[index], patch)
  update({ endpoints })
}

function addEndpoint() {
  const id = Math.random().toString(36).substring(2, 10)
  const endpoints = [...props.modelValue.endpoints, {
    id,
    name: `提供商 ${props.modelValue.endpoints.length + 1}`,
    provider: 'OpenAI' as LlmProvider,
    apiBaseUrl: '',
    apiKey: '',
    modelName: '',
    apiFormat: 'Responses' as LlmApiFormat,
    reasoningEffort: 'None' as LlmReasoningEffort,
    priority: 5,
    enabled: true,
  }]
  update({ endpoints })
}

function removeEndpoint(index: number) {
  const endpoints = props.modelValue.endpoints.filter((_, i) => i !== index)
  update({ endpoints })
}

const providerOptions: { label: string; value: LlmProvider }[] = [
  { label: 'OpenAI', value: 'OpenAI' },
  { label: 'Claude (Anthropic)', value: 'Claude' },
  { label: 'Google Gemini', value: 'Gemini' },
  { label: 'DeepSeek', value: 'DeepSeek' },
  { label: '通义千问 (Qwen)', value: 'Qwen' },
  { label: '智谱清言 (GLM)', value: 'GLM' },
  { label: 'Kimi (Moonshot)', value: 'Kimi' },
  { label: '自定义 (OpenAI 兼容)', value: 'Custom' },
]

const agentEndpointOptions = computed(() => {
  const options: Array<{ label: string; value: string; disabled?: boolean }> = [{
    label: '自动（按优先级选择可用云端提供商）',
    value: AUTOMATIC_AGENT_ENDPOINT,
  }]
  for (const endpoint of props.modelValue.endpoints) {
    if (!endpoint.enabled || !endpoint.apiKey || endpoint.apiKey.toLowerCase() === 'local') continue
    const provider = providerOptions.find(item => item.value === endpoint.provider)?.label ?? endpoint.provider
    const model = endpoint.modelName ? ` · ${endpoint.modelName}` : ''
    options.push({ label: `${provider} · ${endpoint.name || endpoint.id}${model}`, value: endpoint.id })
  }
  const selected = props.modelValue.agentEndpointId
  if (selected && !options.some(option => option.value === selected)) {
    options.push({ label: `原端点 ${selected}（当前不可用）`, value: selected, disabled: true })
  }
  return options
})

const agentEndpointValue = computed(() => props.modelValue.agentEndpointId || AUTOMATIC_AGENT_ENDPOINT)

function updateAgentEndpoint(value: string) {
  update({ agentEndpointId: value === AUTOMATIC_AGENT_ENDPOINT ? null : value })
}

const apiFormatOptions: { label: string; value: LlmApiFormat }[] = [
  { label: 'Responses API（推荐）', value: 'Responses' },
  { label: 'Chat Completions', value: 'ChatCompletions' },
]

const reasoningEffortOptions: { label: string; value: LlmReasoningEffort }[] = [
  { label: '跟随提供商默认', value: 'Default' },
  { label: '关闭思考（none）', value: 'None' },
  { label: '极低（minimal）', value: 'Minimal' },
  { label: '低（low）', value: 'Low' },
  { label: '中（medium）', value: 'Medium' },
  { label: '高（high）', value: 'High' },
  { label: '极高（xhigh）', value: 'XHigh' },
  { label: '最大（max）', value: 'Max' },
]

const defaultBaseUrls: Record<LlmProvider, string> = {
  OpenAI: 'https://api.openai.com/v1',
  Claude: 'https://api.anthropic.com/v1',
  Gemini: 'https://generativelanguage.googleapis.com/v1beta',
  DeepSeek: 'https://api.deepseek.com',
  Qwen: 'https://dashscope.aliyuncs.com/compatible-mode/v1',
  GLM: 'https://open.bigmodel.cn/api/paas/v4',
  Kimi: 'https://api.moonshot.cn/v1',
  Custom: '',
}

const defaultModels: Record<LlmProvider, string> = {
  OpenAI: 'gpt-5.6-luna',
  Claude: 'claude-sonnet-5',
  Gemini: 'gemini-3.6-flash',
  DeepSeek: 'deepseek-v4-flash',
  Qwen: 'qwen3.7-plus',
  GLM: 'glm-5.2',
  Kimi: 'kimi-k2.6',
  Custom: '',
}

const defaultApiFormats: Record<LlmProvider, LlmApiFormat> = {
  OpenAI: 'Responses',
  Claude: 'ChatCompletions',
  Gemini: 'ChatCompletions',
  DeepSeek: 'Responses',
  Qwen: 'Responses',
  GLM: 'ChatCompletions',
  Kimi: 'ChatCompletions',
  Custom: 'ChatCompletions',
}

const defaultReasoningEfforts: Record<LlmProvider, LlmReasoningEffort> = {
  OpenAI: 'None',
  Claude: 'None',
  Gemini: 'None',
  DeepSeek: 'None',
  Qwen: 'None',
  GLM: 'None',
  Kimi: 'None',
  Custom: 'Default',
}

const supportsModelList: Record<LlmProvider, boolean> = {
  OpenAI: true,
  Claude: true,
  Gemini: true,
  DeepSeek: true,
  Qwen: true,
  GLM: true,
  Kimi: true,
  Custom: true,
}

const compatibleProviders = new Set<LlmProvider>([
  'OpenAI', 'DeepSeek', 'Qwen', 'GLM', 'Kimi', 'Custom',
])

const responsesProviders = new Set<LlmProvider>([
  'OpenAI', 'DeepSeek', 'Qwen', 'Custom',
])

function isCompatibleProvider(provider: LlmProvider) {
  return compatibleProviders.has(provider)
}

function getApiFormatOptions(provider: LlmProvider) {
  return responsesProviders.has(provider)
    ? apiFormatOptions
    : apiFormatOptions.filter(option => option.value === 'ChatCompletions')
}

function handleProviderChange(index: number, provider: LlmProvider) {
  updateEndpoint(index, {
    provider,
    apiBaseUrl: '',
    modelName: '',
    apiFormat: defaultApiFormats[provider],
    reasoningEffort: defaultReasoningEfforts[provider],
  })
}

function getApiFormatHint(provider: LlmProvider) {
  if (provider === 'DeepSeek')
    return 'DeepSeek V4 原生支持 Responses API；旧模型可切回 Chat Completions。'
  if (provider === 'Qwen')
    return 'Qwen 新模型推荐 Responses API，可直接使用 reasoning.effort。'
  if (provider === 'OpenAI')
    return 'GPT-5.6 推荐使用 Responses API。'
  if (provider === 'Custom')
    return '仅在自定义服务明确兼容 /responses 时选择 Responses API。'
  return '该提供商当前使用 OpenAI 兼容的 Chat Completions 接口。'
}

function getReasoningHint(endpoint: ApiEndpointConfig) {
  const format = endpoint.apiFormat ?? 'ChatCompletions'
  switch (endpoint.provider) {
    case 'DeepSeek':
      return format === 'Responses'
        ? 'none 会真正关闭思考；minimal/low、medium/high/xhigh、max 会分别映射到 DeepSeek 的低、高、最大档。'
        : 'none 使用 thinking.disabled；其他档位启用思考并映射到 low/high/max。'
    case 'OpenAI':
      return 'GPT-5.6 支持 none、low、medium、high、xhigh、max；minimal 会兼容映射为 low。'
    case 'Qwen':
      return format === 'Responses'
        ? 'none 会关闭思考，其余档位通过 reasoning.effort 控制。'
        : 'Chat Completions 只映射为 enable_thinking 开关；需要精细档位请使用 Responses API。'
    case 'Claude':
      if (endpoint.modelName.startsWith('claude-fable-5') || endpoint.modelName.startsWith('claude-mythos'))
        return '该 Claude 型号为强制自适应思考模型；none 会降到 low，若要真正关闭请改用 Sonnet 5 等支持型号。'
      return 'none 使用 thinking.disabled；其余档位使用 adaptive thinking 与 output_config.effort。'
    case 'Gemini':
      return endpoint.modelName.startsWith('gemini-2.5-flash')
        ? 'Gemini 2.5 Flash 的 none 使用 thinkingBudget=0。'
        : endpoint.modelName.startsWith('gemini-2.5-pro')
          ? 'Gemini 2.5 Pro 无法完全关闭；none 会使用最低 thinkingBudget。'
        : 'Gemini 3 无法完全关闭思考；none 会降到其最低的 minimal 档。'
    case 'GLM':
      return 'none 使用 thinking.disabled；其他档位同时设置 thinking 与 reasoning_effort。'
    case 'Kimi':
      return endpoint.modelName.startsWith('kimi-k3')
        ? 'Kimi K3 为强制思考模型；none 会降到 low，需要真正关闭时请使用 kimi-k2.6。'
        : endpoint.modelName.startsWith('kimi-k2.7-code')
          ? 'Kimi K2.7 Code 为强制思考模型，需要真正关闭时请使用 kimi-k2.6。'
        : 'kimi-k2.6 支持 thinking.enabled/disabled；非 none 档位都会启用思考。'
    default:
      return '自定义服务会接收标准 reasoning_effort；请确认服务端支持所选值。'
  }
}

function getBaseUrlPlaceholder(provider: LlmProvider) {
  return defaultBaseUrls[provider] || '请输入 API 地址'
}

function getModelPlaceholder(provider: LlmProvider) {
  return defaultModels[provider] || '请输入模型名称'
}

function handleRestorePrompt() {
  update({ systemPrompt: DEFAULT_SYSTEM_PROMPT })
}

const modelOptions = ref<Record<string, { label: string; value: string }[]>>({})

async function handleFetchModels(endpoint: ApiEndpointConfig) {
  if (!endpoint.apiKey) {
    message.warning('请先填写 API Key')
    return
  }
  fetchingModels.value = { ...fetchingModels.value, [endpoint.id]: true }
  try {
    const models = await translateApi.fetchModels(
      endpoint.provider,
      endpoint.apiBaseUrl,
      endpoint.apiKey,
    )
    if (models.length > 0) {
      modelOptions.value = {
        ...modelOptions.value,
        [endpoint.id]: models.map(m => ({ label: m, value: m })),
      }
      message.success(`获取到 ${models.length} 个模型`)
    } else {
      message.warning('未获取到模型列表，请手动输入模型名称')
    }
  } catch {
    message.error('获取模型列表失败')
  } finally {
    fetchingModels.value = { ...fetchingModels.value, [endpoint.id]: false }
  }
}

const testingEndpoint = ref<Record<string, boolean>>({})

async function handleTestEndpoint(ep: ApiEndpointConfig) {
  if (!ep.apiKey) {
    message.warning('请先填写 API Key')
    return
  }
  testingEndpoint.value = { ...testingEndpoint.value, [ep.id]: true }
  try {
    const results = await translateApi.testTranslate(
      [ep], props.modelValue.systemPrompt, props.modelValue.temperature,
    )
    const r = results[0]
    if (r && r.success) {
      message.success(`${ep.name || '提供商'} 测试成功: ${r.translations?.join(' | ')} (${Math.round(r.responseTimeMs)}ms)`)
    } else {
      message.error(`${ep.name || '提供商'} 测试失败: ${r?.error || '未知错误'}`)
    }
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : '未知错误'
    message.error(`测试失败: ${msg}`)
  } finally {
    testingEndpoint.value = { ...testingEndpoint.value, [ep.id]: false }
  }
}

async function handleTestAll() {
  const enabled = props.modelValue.endpoints.filter(e => e.enabled && e.apiKey)
  if (enabled.length === 0) {
    message.warning('请先配置至少一个可用的提供商')
    return
  }
  testing.value = true
  try {
    const results = await translateApi.testTranslate(
      enabled, props.modelValue.systemPrompt, props.modelValue.temperature,
    )
    const successCount = results.filter(r => r.success).length
    const totalCount = results.length

    if (successCount === totalCount) {
      const details = results.map(r => `${r.endpointName}: ${r.translations?.join(' | ')} (${Math.round(r.responseTimeMs)}ms)`).join('\n')
      message.success(`全部 ${totalCount} 个提供商测试成功\n${details}`, { duration: 6000 })
    } else {
      const failed = results.filter(r => !r.success)
      const succeeded = results.filter(r => r.success)
      let msg = `${successCount}/${totalCount} 个提供商测试成功`
      if (succeeded.length > 0)
        msg += `\n成功: ${succeeded.map(r => r.endpointName).join(', ')}`
      msg += `\n失败: ${failed.map(r => `${r.endpointName}(${r.error})`).join(', ')}`
      message.warning(msg, { duration: 8000 })
    }
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : '未知错误'
    message.error(`测试失败: ${msg}`)
  } finally {
    testing.value = false
  }
}

const priorityMarks = computed(() => {
  const marks: Record<number, string> = {}
  for (let i = 1; i <= 10; i++) marks[i] = String(i)
  return marks
})
</script>

<template>
  <div :class="{ 'section-card': !embedded }">
    <div v-if="!embedded" class="section-header">
      <h2 class="section-title">
        <span class="section-icon ai">
          <NIcon :size="16"><SmartToyOutlined /></NIcon>
        </span>
        AI 翻译设置
      </h2>
    </div>

    <!-- Global Settings -->
    <div class="ai-form">
      <div class="form-row">
        <label class="form-label">最大并发数（{{ modelValue.maxConcurrency }}）</label>
        <NSlider
          :value="modelValue.maxConcurrency"
          @update:value="(v: number) => update({ maxConcurrency: v })"
          :min="1"
          :max="100"
          :step="1"
          :tooltip="true"
        />
        <span class="form-hint">同时进行的 LLM API 调用数量</span>
      </div>

      <div class="form-row">
        <label class="form-label">系统提示词</label>
        <div class="prompt-wrapper">
          <NInput
            :value="modelValue.systemPrompt"
            @update:value="(v: string) => update({ systemPrompt: v })"
            type="textarea"
            :rows="3"
            placeholder="指导 LLM 如何翻译"
          />
          <NButton
            size="tiny"
            quaternary
            class="restore-btn"
            @click="handleRestorePrompt"
          >
            <template #icon>
              <NIcon :size="14"><RestoreOutlined /></NIcon>
            </template>
            恢复默认
          </NButton>
        </div>
        <span class="form-hint">使用 {from} 和 {to} 作为语言占位符</span>
      </div>

      <div class="form-row">
        <label class="form-label">温度（{{ modelValue.temperature }}）</label>
        <NSlider
          :value="modelValue.temperature"
          @update:value="(v: number) => update({ temperature: v })"
          :min="0"
          :max="2"
          :step="0.1"
          :tooltip="true"
          :format-tooltip="(v: number) => v.toFixed(1)"
        />
        <span class="form-hint">较低的值产生更确定的翻译；启用思考时，部分新模型会忽略或不接收温度参数</span>
      </div>

      <div class="form-row">
        <label class="form-label">翻译上下文条数（{{ modelValue.contextSize }}）</label>
        <NSlider
          :value="modelValue.contextSize"
          @update:value="(v: number) => update({ contextSize: v })"
          :min="0"
          :max="100"
          :step="1"
          :tooltip="true"
        />
        <span class="form-hint">附带的近期翻译对数量作为上下文，0 为关闭</span>
      </div>
    </div>

    <!-- Endpoints -->
    <div class="endpoints-section">
      <div class="endpoints-header">
        <h3 class="subsection-title">AI 提供商</h3>
        <NButton size="small" @click="addEndpoint">
          <template #icon>
            <NIcon><AddOutlined /></NIcon>
          </template>
          添加提供商
        </NButton>
      </div>

      <div v-if="modelValue.endpoints.length === 0" class="empty-endpoints">
        <p>尚未配置任何 AI 提供商，请点击"添加提供商"开始配置。</p>
      </div>

      <NCollapse v-else>
        <NCollapseItem
          v-for="(ep, index) in modelValue.endpoints"
          :key="ep.id"
          :name="ep.id"
        >
          <template #header>
            <div class="endpoint-header">
              <NSwitch
                :value="ep.enabled"
                @update:value="(v: boolean) => updateEndpoint(index, { enabled: v })"
                size="small"
                @click.stop
              />
              <span class="endpoint-name">{{ ep.name || `提供商 ${index + 1}` }}</span>
              <NTag size="small" :type="ep.enabled ? 'success' : 'default'">
                {{ providerOptions.find(p => p.value === ep.provider)?.label || ep.provider }}
              </NTag>
              <NTag
                v-if="isCompatibleProvider(ep.provider) && (ep.apiFormat ?? 'ChatCompletions') === 'Responses'"
                size="small"
                type="warning"
              >
                Responses
              </NTag>
              <NTag v-if="ep.enabled && ep.apiKey" size="small" type="info">
                优先级 {{ ep.priority }}
              </NTag>
            </div>
          </template>
          <template #header-extra>
            <NPopconfirm @positive-click="removeEndpoint(index)">
              <template #trigger>
                <NButton size="tiny" quaternary type="error" @click.stop>
                  <template #icon>
                    <NIcon><DeleteOutlined /></NIcon>
                  </template>
                </NButton>
              </template>
              确定删除此提供商？
            </NPopconfirm>
          </template>

          <div class="endpoint-form">
            <div class="form-row">
              <label class="form-label">名称</label>
              <NInput
                :value="ep.name"
                @update:value="(v: string) => updateEndpoint(index, { name: v })"
                placeholder="提供商名称"
              />
            </div>

            <div class="form-row">
              <label class="form-label">LLM 提供商</label>
              <NSelect
                :value="ep.provider"
                @update:value="(v: LlmProvider) => handleProviderChange(index, v)"
                :options="providerOptions"
              />
            </div>

            <div v-if="isCompatibleProvider(ep.provider)" class="form-row">
              <label class="form-label">API 格式</label>
              <NSelect
                :value="ep.apiFormat ?? 'ChatCompletions'"
                @update:value="(v: LlmApiFormat) => updateEndpoint(index, { apiFormat: v })"
                :options="getApiFormatOptions(ep.provider)"
                :disabled="!responsesProviders.has(ep.provider)"
              />
              <span class="form-hint">{{ getApiFormatHint(ep.provider) }}</span>
            </div>

            <div class="form-row">
              <label class="form-label">API 地址</label>
              <NInput
                :value="ep.apiBaseUrl"
                @update:value="(v: string) => updateEndpoint(index, { apiBaseUrl: v })"
                :placeholder="getBaseUrlPlaceholder(ep.provider)"
                clearable
              />
              <span class="form-hint">留空使用默认地址</span>
            </div>

            <div class="form-row">
              <label class="form-label">API Key</label>
              <NInput
                :value="ep.apiKey"
                @update:value="(v: string) => updateEndpoint(index, { apiKey: v })"
                placeholder="输入 API Key"
                type="password"
                show-password-on="click"
                clearable
              />
            </div>

            <div class="form-row">
              <label class="form-label">模型名称</label>
              <div class="model-row">
                <NSelect
                  v-if="modelOptions[ep.id]?.length"
                  :value="ep.modelName || undefined"
                  @update:value="(v: string) => updateEndpoint(index, { modelName: v })"
                  :options="modelOptions[ep.id]"
                  filterable
                  clearable
                  tag
                  :placeholder="getModelPlaceholder(ep.provider)"
                />
                <NInput
                  v-else
                  :value="ep.modelName"
                  @update:value="(v: string) => updateEndpoint(index, { modelName: v })"
                  :placeholder="getModelPlaceholder(ep.provider)"
                  clearable
                />
                <NButton
                  v-if="supportsModelList[ep.provider]"
                  size="small"
                  :loading="fetchingModels[ep.id]"
                  @click="handleFetchModels(ep)"
                >
                  <template #icon>
                    <NIcon><SearchOutlined /></NIcon>
                  </template>
                </NButton>
              </div>
              <span class="form-hint">留空使用默认模型{{ supportsModelList[ep.provider] ? '，点击搜索图标获取模型列表' : '' }}</span>
            </div>

            <div class="form-row">
              <label class="form-label">思考模式 / 强度</label>
              <NSelect
                :value="ep.reasoningEffort ?? 'Default'"
                @update:value="(v: LlmReasoningEffort) => updateEndpoint(index, { reasoningEffort: v })"
                :options="reasoningEffortOptions"
              />
              <span class="form-hint">{{ getReasoningHint(ep) }}</span>
            </div>

            <div class="form-row">
              <label class="form-label">优先级（{{ ep.priority }}）</label>
              <NSlider
                :value="ep.priority"
                @update:value="(v: number) => updateEndpoint(index, { priority: v })"
                :min="1"
                :max="10"
                :step="1"
                :marks="priorityMarks"
              />
              <span class="form-hint">数值越高优先级越高，负载均衡时优先使用高优先级提供商</span>
            </div>

            <div class="form-row">
              <NButton
                size="small"
                :loading="testingEndpoint[ep.id]"
                @click="handleTestEndpoint(ep)"
                ghost
              >
                <template #icon>
                  <NIcon><ScienceOutlined /></NIcon>
                </template>
                测试此提供商
              </NButton>
            </div>
          </div>
        </NCollapseItem>
      </NCollapse>

      <div class="agent-endpoint-setting">
        <div class="form-row">
          <label class="form-label">工具箱智能体使用的提供商</label>
          <NSelect
            :value="agentEndpointValue"
            :options="agentEndpointOptions"
            @update:value="updateAgentEndpoint"
            placeholder="选择工具箱智能体提供商"
          />
          <span class="form-hint">统一用于工具箱对话、插件智能诊断和修复规划；指定端点失效时不会自动回退。</span>
        </div>
        <NAlert type="warning" :bordered="false">
          工具箱智能体可把已添加游戏目录和工具箱数据目录中的原始文件内容、绝对路径和工具输出发送给所选云端提供商。读取其他电脑路径或运行脚本前会逐次请求确认。
        </NAlert>
      </div>
    </div>

    <div class="section-footer">
      <span></span>
      <NButton
        :loading="testing"
        @click="handleTestAll"
        ghost
      >
        <template #icon>
          <NIcon><ScienceOutlined /></NIcon>
        </template>
        测试所有提供商
      </NButton>
    </div>
  </div>
</template>

<style scoped>
.section-card {
  display: flex;
  flex-direction: column;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 24px;
  animation: slideUp 0.5s var(--ease-out) backwards;
  transition: border-color 0.3s ease, background 0.3s ease, box-shadow 0.3s ease;
  box-shadow: var(--shadow-card-rest);
}

.section-card:hover {
  border-color: var(--border-hover);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
}

.section-title {
  font-family: var(--font-display);
  font-size: 17px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.01em;
  display: flex;
  align-items: center;
  gap: 10px;
}

.section-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border-radius: 8px;
  flex-shrink: 0;
}

.section-icon.ai {
  background: var(--accent-soft);
  color: var(--accent);
}

.ai-form {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.form-row-inline {
  display: flex;
  gap: 20px;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.form-label {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-2);
  display: flex;
  align-items: center;
  gap: 6px;
}

.form-hint {
  font-size: 12px;
  color: var(--text-3);
}

.prompt-wrapper {
  position: relative;
}

.restore-btn {
  position: absolute;
  top: 6px;
  right: 6px;
  opacity: 0.6;
  transition: opacity 0.2s ease;
}

.restore-btn:hover {
  opacity: 1;
}

/* Endpoints Section */
.endpoints-section {
  margin-top: 24px;
  padding-top: 20px;
  border-top: 1px solid var(--border);
}

.endpoints-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.subsection-title {
  font-family: var(--font-display);
  font-size: 15px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
}

.empty-endpoints {
  padding: 24px;
  text-align: center;
  color: var(--text-3);
  background: var(--bg-subtle);
  border-radius: var(--radius-md);
  border: 1px dashed var(--border);
}

.empty-endpoints p {
  margin: 0;
  font-size: 13px;
}

.endpoint-header {
  display: flex;
  align-items: center;
  gap: 10px;
}

.endpoint-name {
  font-weight: 500;
  color: var(--text-1);
}

.endpoint-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 4px 0;
}

.agent-endpoint-setting {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 16px;
  padding: 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.model-row {
  display: flex;
  gap: 8px;
  align-items: center;
}

.model-row > *:first-child {
  flex: 1;
}

.section-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 16px;
  margin-top: 20px;
  border-top: 1px solid var(--border);
  gap: 12px;
}

@media (max-width: 768px) {
  .section-card {
    padding: 16px;
  }

  .form-row-inline {
    flex-direction: column;
    gap: 16px;
  }

  .form-row :deep(.n-input-number) {
    width: 100% !important;
  }
}

@media (max-width: 480px) {
  .section-card {
    padding: 14px;
    border-radius: var(--radius-md);
  }

  .section-footer {
    flex-direction: column;
    align-items: stretch;
    gap: 10px;
  }
}
</style>
