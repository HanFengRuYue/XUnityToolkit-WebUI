<script setup lang="ts">
import { ref, computed } from 'vue'
import {
  NSelect,
  NInput,
  NInputNumber,
  NButton,
  NIcon,
  useMessage,
} from 'naive-ui'
import { SmartToyOutlined, RestoreOutlined, ScienceOutlined } from '@vicons/material'
import type { AiTranslationSettings, LlmProvider } from '@/api/types'
import { translateApi } from '@/api/games'

const props = defineProps<{
  modelValue: AiTranslationSettings
  saving: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: AiTranslationSettings]
  save: []
}>()

const message = useMessage()
const testing = ref(false)

const DEFAULT_SYSTEM_PROMPT =
  'You are a professional game text translator. ' +
  'Translate the following texts from {from} to {to}. ' +
  'Return ONLY a JSON array of translated strings in the same order as the input. ' +
  'Do not add any explanation or markdown formatting. ' +
  'Example input: ["Hello","World"] Example output: ["你好","世界"]'

function update(patch: Partial<AiTranslationSettings>) {
  emit('update:modelValue', { ...props.modelValue, ...patch })
}

const providerOptions = [
  { label: 'OpenAI', value: 'OpenAI' },
  { label: 'Claude (Anthropic)', value: 'Claude' },
  { label: 'Google Gemini', value: 'Gemini' },
  { label: '自定义 (OpenAI 兼容)', value: 'Custom' },
]

const defaultBaseUrls: Record<LlmProvider, string> = {
  OpenAI: 'https://api.openai.com/v1',
  Claude: 'https://api.anthropic.com/v1',
  Gemini: 'https://generativelanguage.googleapis.com/v1beta',
  Custom: '',
}

const defaultModels: Record<LlmProvider, string> = {
  OpenAI: 'gpt-4o-mini',
  Claude: 'claude-haiku-4-5-20251001',
  Gemini: 'gemini-2.0-flash',
  Custom: '',
}

const baseUrlPlaceholder = computed(() =>
  defaultBaseUrls[props.modelValue.provider] || '请输入 API 地址'
)

const modelPlaceholder = computed(() =>
  defaultModels[props.modelValue.provider] || '请输入模型名称'
)

const isCustomProvider = computed(() => props.modelValue.provider === 'Custom')

function handleRestorePrompt() {
  update({ systemPrompt: DEFAULT_SYSTEM_PROMPT })
}

async function handleTest() {
  if (!props.modelValue.apiKey) {
    message.warning('请先填写 API Key')
    return
  }
  testing.value = true
  try {
    const result = await translateApi.translate(['Hello, world!', 'Good morning'], 'en', 'zh')
    if (result.translations && result.translations.length > 0) {
      message.success(`测试成功: ${result.translations.join(' | ')}`)
    } else {
      message.error('测试失败: 未返回翻译结果')
    }
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : '未知错误'
    message.error(`测试失败: ${msg}`)
  } finally {
    testing.value = false
  }
}
</script>

<template>
  <div class="section-card">
    <div class="section-header">
      <h2 class="section-title">
        <span class="section-icon ai">
          <NIcon :size="16"><SmartToyOutlined /></NIcon>
        </span>
        AI 翻译
      </h2>
    </div>

    <div class="ai-form">
      <div class="form-row">
        <label class="form-label">LLM 提供商</label>
        <NSelect
          :value="modelValue.provider"
          @update:value="(v: LlmProvider) => update({ provider: v })"
          :options="providerOptions"
        />
      </div>

      <div class="form-row">
        <label class="form-label">API 地址</label>
        <NInput
          :value="modelValue.apiBaseUrl"
          @update:value="(v: string) => update({ apiBaseUrl: v })"
          :placeholder="baseUrlPlaceholder"
          clearable
        />
        <span class="form-hint">
          {{ isCustomProvider ? '请输入 OpenAI 兼容的 API 地址' : '留空使用默认地址' }}
        </span>
      </div>

      <div class="form-row">
        <label class="form-label">API Key</label>
        <NInput
          :value="modelValue.apiKey"
          @update:value="(v: string) => update({ apiKey: v })"
          placeholder="输入 API Key"
          type="password"
          show-password-on="click"
          clearable
        />
      </div>

      <div class="form-row">
        <label class="form-label">模型名称</label>
        <NInput
          :value="modelValue.modelName"
          @update:value="(v: string) => update({ modelName: v })"
          :placeholder="modelPlaceholder"
          clearable
        />
        <span class="form-hint">留空使用默认模型</span>
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
        <label class="form-label">温度</label>
        <NInputNumber
          :value="modelValue.temperature"
          @update:value="(v: number | null) => update({ temperature: v ?? 0.3 })"
          :min="0"
          :max="2"
          :step="0.1"
          style="width: 140px"
        />
        <span class="form-hint">较低的值产生更确定的翻译</span>
      </div>
    </div>

    <div class="section-footer">
      <NButton
        :loading="testing"
        @click="handleTest"
        ghost
      >
        <template #icon>
          <NIcon><ScienceOutlined /></NIcon>
        </template>
        测试翻译
      </NButton>
      <NButton type="primary" :loading="saving" @click="emit('save')">
        保存设置
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
  background: rgba(167, 139, 250, 0.10);
  color: #a78bfa;
}

.ai-form {
  display: flex;
  flex-direction: column;
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
