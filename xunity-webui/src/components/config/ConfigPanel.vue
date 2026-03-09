<script setup lang="ts">
import { ref, watch } from 'vue'
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
</script>

<template>
  <NForm :disabled="disabled" label-placement="left" label-width="160">
    <NFormItem label="源语言">
      <NSelect v-model:value="form.sourceLanguage" :options="languageOptions" />
    </NFormItem>
    <NFormItem label="目标语言">
      <NSelect v-model:value="form.targetLanguage" :options="languageOptions" />
    </NFormItem>
    <NFormItem label="翻译引擎">
      <NSelect v-model:value="form.translationEngine" :options="engineOptions" />
    </NFormItem>

    <NCollapse style="margin-top: 8px">
      <NCollapseItem title="高级选项" name="advanced">
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

    <NSpace justify="end" style="margin-top: 16px">
      <NButton type="primary" :disabled="disabled" @click="handleSave">
        保存配置
      </NButton>
    </NSpace>
  </NForm>
</template>
