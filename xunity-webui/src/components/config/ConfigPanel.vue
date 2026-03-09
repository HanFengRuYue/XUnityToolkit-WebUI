<script setup lang="ts">
import { ref, watch, computed, onMounted, onUnmounted } from 'vue'
import {
  NForm,
  NFormItem,
  NSelect,
  NSwitch,
  NInputNumber,
  NCollapse,
  NCollapseItem,
  NButton,
  NSpace,
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

      <NCollapse style="margin-top: 4px">
        <NCollapseItem title="高级选项" name="advanced">
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
          <NFormItem label="每次最大翻译字符数">
            <NInputNumber v-model:value="form.maxCharactersPerTranslation" :min="10" :max="5000" />
          </NFormItem>
          <NFormItem label="处理富文本">
            <NSwitch v-model:value="form.handleRichText" />
          </NFormItem>
          <NFormItem label="启用 UI 缩放">
            <NSwitch v-model:value="form.enableUIResizing" />
          </NFormItem>
        </NCollapseItem>
      </NCollapse>

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
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
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
@media (max-width: 768px) {
  .advanced-grid {
    grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
    gap: 0 16px;
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
