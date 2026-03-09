<script setup lang="ts">
import { ref, watch, computed, onMounted, onUnmounted } from 'vue'
import {
  NForm,
  NFormItem,
  NSelect,
  NSwitch,
  NInputNumber,
  NInput,
  NCollapse,
  NCollapseItem,
  NButton,
} from 'naive-ui'
import type { XUnityConfig } from '@/api/types'

const props = defineProps<{
  config: XUnityConfig | null
  disabled: boolean
}>()

const emit = defineEmits<{
  save: [config: XUnityConfig]
}>()

const form = ref<XUnityConfig>({
  sourceLanguage: 'ja',
  targetLanguage: 'zh',
  translationEngine: 'GoogleTranslateV2',
  fallbackEndpoint: '',
  enableUGUI: true,
  enableNGUI: true,
  enableTextMeshPro: true,
  enableTextMesh: false,
  enableIMGUI: false,
  maxCharactersPerTranslation: 200,
  handleRichText: true,
  enableUIResizing: true,
  extra: {},
})

interface EngineKeyField {
  key: keyof XUnityConfig
  label: string
  placeholder: string
  type: 'text' | 'password'
}

const engineKeyFields = computed<EngineKeyField[]>(() => {
  switch (form.value.translationEngine) {
    case 'GoogleTranslateV2':
      return [{ key: 'googleTranslateV2ApiKey', label: 'API Key', placeholder: 'Google Cloud API Key', type: 'password' }]
    case 'BingTranslate':
      return [{ key: 'bingTranslateOcpApimSubscriptionKey', label: 'Subscription Key', placeholder: 'Azure 订阅密钥', type: 'password' }]
    case 'BaiduTranslate':
      return [
        { key: 'baiduTranslateAppId', label: 'App ID', placeholder: '百度翻译 AppId', type: 'text' },
        { key: 'baiduTranslateAppSecret', label: 'App Secret', placeholder: '百度翻译密钥', type: 'password' },
      ]
    case 'YandexTranslate':
      return [{ key: 'yandexTranslateApiKey', label: 'API Key', placeholder: 'Yandex API Key', type: 'password' }]
    case 'DeepLTranslate':
      return [{ key: 'deepLTranslateApiKey', label: 'API Key', placeholder: 'DeepL Auth Key', type: 'password' }]
    case 'PapagoTranslate':
      return [
        { key: 'papagoTranslateClientId', label: 'Client ID', placeholder: 'Papago Client ID', type: 'text' },
        { key: 'papagoTranslateClientSecret', label: 'Client Secret', placeholder: 'Papago 密钥', type: 'password' },
      ]
    default:
      return []
  }
})

watch(
  () => props.config,
  (cfg) => {
    if (cfg) form.value = { ...cfg }
  },
  { immediate: true },
)

const languageOptions = [
  { label: '日语 (ja)', value: 'ja' },
  { label: '英语 (en)', value: 'en' },
  { label: '中文 (zh)', value: 'zh' },
  { label: '中文繁体 (zh-TW)', value: 'zh-TW' },
  { label: '韩语 (ko)', value: 'ko' },
  { label: '法语 (fr)', value: 'fr' },
  { label: '德语 (de)', value: 'de' },
  { label: '西班牙语 (es)', value: 'es' },
  { label: '俄语 (ru)', value: 'ru' },
]

const engineOptions = [
  { label: 'Google Translate V2', value: 'GoogleTranslateV2' },
  { label: 'Google Translate V1', value: 'GoogleTranslate' },
  { label: 'Bing Translator', value: 'BingTranslate' },
  { label: 'Baidu Translate', value: 'BaiduTranslate' },
  { label: 'Yandex Translate', value: 'YandexTranslate' },
  { label: 'DeepL Translate', value: 'DeepLTranslate' },
  { label: 'Papago', value: 'PapagoTranslate' },
]

function handleSave() {
  emit('save', { ...form.value })
}

const isMobile = ref(false)
function checkMobile() {
  isMobile.value = window.innerWidth <= 768
}
onMounted(() => {
  checkMobile()
  window.addEventListener('resize', checkMobile)
})
onUnmounted(() => {
  window.removeEventListener('resize', checkMobile)
})

const labelPlacement = computed(() => isMobile.value ? 'top' as const : 'left' as const)
const labelWidth = computed(() => isMobile.value ? undefined : '160')
</script>

<template>
  <div class="config-panel">
    <NForm :disabled="disabled" :label-placement="labelPlacement" :label-width="labelWidth">
      <div class="config-main-grid">
        <div class="config-column">
          <div class="config-section">
            <div class="config-section-label">语言设置</div>
            <NFormItem label="源语言">
              <NSelect v-model:value="form.sourceLanguage" :options="languageOptions" />
            </NFormItem>
            <NFormItem label="目标语言">
              <NSelect v-model:value="form.targetLanguage" :options="languageOptions" />
            </NFormItem>
          </div>

          <div class="config-section">
            <div class="config-section-label">翻译引擎</div>
            <NFormItem label="翻译引擎">
              <NSelect v-model:value="form.translationEngine" :options="engineOptions" />
            </NFormItem>
          </div>

          <div v-if="engineKeyFields.length > 0" class="config-section">
            <div class="config-section-label">API 密钥</div>
            <NFormItem
              v-for="field in engineKeyFields"
              :key="field.key"
              :label="field.label"
            >
              <NInput
                :value="(form[field.key] as string) ?? ''"
                @update:value="(v: string) => { (form as Record<string, unknown>)[field.key] = v || undefined }"
                :placeholder="field.placeholder"
                :type="field.type"
                :show-password-on="field.type === 'password' ? 'click' : undefined"
                clearable
              />
            </NFormItem>
          </div>
        </div>

        <div class="config-column">
          <div class="config-section">
            <div class="config-section-label">文本框架</div>
            <div class="advanced-grid">
              <NFormItem label="UGUI">
                <NSwitch v-model:value="form.enableUGUI" />
              </NFormItem>
              <NFormItem label="NGUI">
                <NSwitch v-model:value="form.enableNGUI" />
              </NFormItem>
              <NFormItem label="TextMeshPro">
                <NSwitch v-model:value="form.enableTextMeshPro" />
              </NFormItem>
              <NFormItem label="TextMesh">
                <NSwitch v-model:value="form.enableTextMesh" />
              </NFormItem>
              <NFormItem label="IMGUI">
                <NSwitch v-model:value="form.enableIMGUI" />
              </NFormItem>
            </div>
          </div>

          <div class="config-section">
            <div class="config-section-label">高级选项</div>
            <NFormItem label="每次最大翻译字符数">
              <NInputNumber v-model:value="form.maxCharactersPerTranslation" :min="10" :max="5000" />
            </NFormItem>
            <NFormItem label="处理富文本">
              <NSwitch v-model:value="form.handleRichText" />
            </NFormItem>
            <NFormItem label="启用 UI 缩放">
              <NSwitch v-model:value="form.enableUIResizing" />
            </NFormItem>
          </div>
        </div>
      </div>

      <div class="config-footer">
        <NButton type="primary" :disabled="disabled" @click="handleSave">
          保存配置
        </NButton>
      </div>
    </NForm>
  </div>
</template>

<style scoped>
.config-panel {
  animation: fadeIn 0.3s ease;
}

.config-main-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px 40px;
}

.config-column {
  display: flex;
  flex-direction: column;
}

.config-section {
  margin-bottom: 8px;
}

.config-section-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  margin-bottom: 12px;
}

.advanced-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0 24px;
}

.config-footer {
  display: flex;
  justify-content: flex-end;
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid var(--border);
}

/* ===== Responsive ===== */
@media (max-width: 960px) {
  .config-main-grid {
    grid-template-columns: 1fr;
    gap: 0;
  }
}

@media (max-width: 480px) {
  .advanced-grid {
    grid-template-columns: 1fr 1fr;
    gap: 0 12px;
  }

  .config-footer .n-button {
    width: 100%;
  }

  .config-footer {
    justify-content: stretch;
  }
}
</style>
