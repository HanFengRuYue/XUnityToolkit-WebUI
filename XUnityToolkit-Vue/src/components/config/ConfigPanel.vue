<script setup lang="ts">
import { ref, watch, computed, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
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
  NIcon,
} from 'naive-ui'
import { EditNoteOutlined } from '@vicons/material'
import type { XUnityConfig } from '@/api/types'

const props = defineProps<{
  config: XUnityConfig | null
  disabled: boolean
  gameId: string
}>()

const router = useRouter()

const emit = defineEmits<{
  save: [config: XUnityConfig]
}>()

const form = ref<XUnityConfig>({
  translationEngine: 'GoogleTranslateV2',
  fallbackEndpoint: '',
  sourceLanguage: 'ja',
  targetLanguage: 'zh',
  outputFile: '',
  enableIMGUI: true,
  enableUGUI: true,
  enableNGUI: true,
  enableTextMeshPro: true,
  enableTextMesh: true,
  enableFairyGUI: true,
  maxCharactersPerTranslation: 2500,
  forceSplitTextAfterCharacters: 0,
  handleRichText: true,
  enableUIResizing: true,
  overrideFont: 'Microsoft YaHei',
  overrideFontSize: '',
  overrideFontTextMeshPro: '',
  fallbackFontTextMeshPro: '',
  resizeUILineSpacingScale: '',
  forceUIResizing: true,
  textGetterCompatibilityMode: false,
  maxTextParserRecursion: 4,
  enableTranslationHelper: false,
  templateAllNumberAway: false,
  disableTextMeshProScrollInEffects: false,
  cacheParsedTranslations: true,
  textureDirectory: '',
  enableTextureTranslation: false,
  enableTextureDumping: false,
  enableTextureToggling: false,
  enableTextureScanOnSceneLoad: false,
  loadUnmodifiedTextures: false,
  textureHashGenerationStrategy: 'FromImageName',
  enableSpriteHooking: false,
  extra: {},
  deepLLegitimateFree: false,
})

interface EngineKeyField {
  key: keyof XUnityConfig
  label: string
  placeholder: string
  type: 'text' | 'password'
}

function getKeyFieldsForEngine(engine: string): EngineKeyField[] {
  switch (engine) {
    case 'GoogleTranslateLegitimate':
      return [{ key: 'googleLegitimateApiKey', label: 'API Key', placeholder: 'Google Cloud API Key', type: 'password' }]
    case 'BingTranslateLegitimate':
      return [{ key: 'bingLegitimateSubscriptionKey', label: 'Subscription Key', placeholder: 'Azure 订阅密钥', type: 'password' }]
    case 'BaiduTranslate':
      return [
        { key: 'baiduAppId', label: 'App ID', placeholder: '百度翻译 AppId', type: 'text' },
        { key: 'baiduAppSecret', label: 'App Secret', placeholder: '百度翻译密钥', type: 'password' },
      ]
    case 'YandexTranslate':
      return [{ key: 'yandexApiKey', label: 'API Key', placeholder: 'Yandex API Key', type: 'password' }]
    case 'DeepLTranslateLegitimate':
      return [{ key: 'deepLLegitimateApiKey', label: 'API Key', placeholder: 'DeepL Auth Key', type: 'password' }]
    case 'PapagoTranslate':
      return [
        { key: 'papagoClientId', label: 'Client ID', placeholder: 'Papago Client ID', type: 'text' },
        { key: 'papagoClientSecret', label: 'Client Secret', placeholder: 'Papago 密钥', type: 'password' },
      ]
    case 'LingoCloudTranslate':
      return [{ key: 'lingoCloudToken', label: 'API Token', placeholder: '彩云小译 Token', type: 'password' }]
    case 'WatsonTranslate':
      return [
        { key: 'watsonUrl', label: '服务 URL', placeholder: 'Watson 翻译服务 URL', type: 'text' },
        { key: 'watsonKey', label: 'API Key', placeholder: 'Watson API Key', type: 'password' },
      ]
    case 'CustomTranslate':
      return [{ key: 'customTranslateUrl', label: '端点 URL', placeholder: '自定义翻译服务 URL', type: 'text' }]
    case 'LecPowerTranslator15':
      return [{ key: 'lecInstallPath', label: '安装路径', placeholder: 'LEC 安装目录路径', type: 'text' }]
    case 'ezTrans':
      return [{ key: 'ezTransInstallPath', label: '安装路径', placeholder: 'ezTrans XP 安装目录路径', type: 'text' }]
    default:
      return []
  }
}

function getEngineName(engine: string): string {
  return engineOptions.find(o => o.value === engine)?.label ?? engine
}

const primaryKeyFields = computed(() => getKeyFieldsForEngine(form.value.translationEngine))
const fallbackKeyFields = computed(() => {
  const fallback = form.value.fallbackEndpoint
  if (!fallback || fallback === form.value.translationEngine) return []
  return getKeyFieldsForEngine(fallback)
})

const showPrimaryDeepLFreeToggle = computed(() => form.value.translationEngine === 'DeepLTranslateLegitimate')
const showFallbackDeepLFreeToggle = computed(() => {
  const fallback = form.value.fallbackEndpoint
  return fallback === 'DeepLTranslateLegitimate' && fallback !== form.value.translationEngine
})

const hasBothKeyGroups = computed(() => primaryKeyFields.value.length > 0 && fallbackKeyFields.value.length > 0)
const hasAnyKeyFields = computed(() =>
  primaryKeyFields.value.length > 0 || fallbackKeyFields.value.length > 0 ||
  showPrimaryDeepLFreeToggle.value || showFallbackDeepLFreeToggle.value
)

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
  { label: '简体中文 (zh_CN)', value: 'zh_CN' },
  { label: '繁体中文 (zh-TW)', value: 'zh-TW' },
  { label: '韩语 (ko)', value: 'ko' },
  { label: '法语 (fr)', value: 'fr' },
  { label: '德语 (de)', value: 'de' },
  { label: '西班牙语 (es)', value: 'es' },
  { label: '俄语 (ru)', value: 'ru' },
]

const engineOptions = [
  { label: 'Google Translate', value: 'GoogleTranslate' },
  { label: 'Google Translate V2', value: 'GoogleTranslateV2' },
  { label: 'Google Translate (兼容)', value: 'GoogleTranslateCompat' },
  { label: 'Google Translate (官方)', value: 'GoogleTranslateLegitimate' },
  { label: 'Bing Translate', value: 'BingTranslate' },
  { label: 'Bing Translate (官方)', value: 'BingTranslateLegitimate' },
  { label: 'DeepL Translate', value: 'DeepLTranslate' },
  { label: 'DeepL Translate (官方)', value: 'DeepLTranslateLegitimate' },
  { label: 'Baidu Translate', value: 'BaiduTranslate' },
  { label: 'Yandex Translate', value: 'YandexTranslate' },
  { label: 'Papago', value: 'PapagoTranslate' },
  { label: '彩云小译', value: 'LingoCloudTranslate' },
  { label: 'IBM Watson', value: 'WatsonTranslate' },
  { label: 'LEC Power Translator', value: 'LecPowerTranslator15' },
  { label: 'ezTrans XP', value: 'ezTrans' },
  { label: '自定义端点', value: 'CustomTranslate' },
]

const textureHashOptions = [
  { label: '按图片名称', value: 'FromImageName' },
  { label: '按图片数据', value: 'FromImageData' },
  { label: '按图片名称和场景', value: 'FromImageNameAndScene' },
]

function handleSave() {
  emit('save', { ...form.value })
}

function openConfigEditor() {
  router.push(`/games/${props.gameId}/config-editor`)
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
            <NFormItem label="备用引擎">
              <NSelect
                v-model:value="form.fallbackEndpoint"
                :options="[{ label: '(无)', value: '' }, ...engineOptions]"
                clearable
              />
            </NFormItem>
          </div>

          <!-- Primary engine API keys -->
          <div v-if="hasAnyKeyFields" class="config-section">
            <div v-if="primaryKeyFields.length > 0 || showPrimaryDeepLFreeToggle" class="api-key-group">
              <div class="config-section-label">
                {{ fallbackKeyFields.length > 0 || showFallbackDeepLFreeToggle ? `API 密钥 — ${getEngineName(form.translationEngine)}` : 'API 密钥' }}
              </div>
              <NFormItem
                v-for="field in primaryKeyFields"
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
              <NFormItem v-if="showPrimaryDeepLFreeToggle" label="使用免费版 API">
                <NSwitch v-model:value="form.deepLLegitimateFree" />
              </NFormItem>
            </div>

            <!-- Fallback engine API keys (only when different engine) -->
            <div v-if="fallbackKeyFields.length > 0 || showFallbackDeepLFreeToggle" class="api-key-group">
              <div class="config-section-label">
                {{ primaryKeyFields.length > 0 || showPrimaryDeepLFreeToggle ? `API 密钥 — ${getEngineName(form.fallbackEndpoint ?? '')}` : 'API 密钥' }}
              </div>
              <NFormItem
                v-for="field in fallbackKeyFields"
                :key="'fallback-' + field.key"
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
              <NFormItem v-if="showFallbackDeepLFreeToggle" label="使用免费版 API">
                <NSwitch v-model:value="form.deepLLegitimateFree" />
              </NFormItem>
            </div>
          </div>
        </div>

        <div class="config-column">
          <div class="config-section">
            <div class="config-section-label">文本框架</div>
            <div class="framework-grid">
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
              <NFormItem label="FairyGUI">
                <NSwitch v-model:value="form.enableFairyGUI" />
              </NFormItem>
            </div>
          </div>

          <div class="config-section">
            <div class="config-section-label">基本设置</div>
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

      <!-- Collapsible advanced sections -->
      <NCollapse class="config-collapse">
        <NCollapseItem title="字体与排版" name="font">
          <div class="collapse-grid">
            <NFormItem label="覆盖字体">
              <NInput
                :value="form.overrideFont ?? ''"
                @update:value="(v: string) => { form.overrideFont = v }"
                placeholder="UGUI 自定义字体名称" clearable
              />
            </NFormItem>
            <NFormItem label="覆盖字体大小">
              <NInput
                :value="form.overrideFontSize ?? ''"
                @update:value="(v: string) => { form.overrideFontSize = v }"
                placeholder="UGUI 自定义字体大小" clearable
              />
            </NFormItem>
            <NFormItem label="TMP 覆盖字体">
              <NInput
                :value="form.overrideFontTextMeshPro ?? ''"
                @update:value="(v: string) => { form.overrideFontTextMeshPro = v }"
                placeholder="TextMeshPro 自定义字体" clearable
              />
            </NFormItem>
            <NFormItem label="TMP 后备字体">
              <NInput
                :value="form.fallbackFontTextMeshPro ?? ''"
                @update:value="(v: string) => { form.fallbackFontTextMeshPro = v }"
                placeholder="TextMeshPro 后备字体" clearable
              />
            </NFormItem>
            <NFormItem label="行间距缩放">
              <NInput
                :value="form.resizeUILineSpacingScale ?? ''"
                @update:value="(v: string) => { form.resizeUILineSpacingScale = v }"
                placeholder="例如 0.80" clearable
              />
            </NFormItem>
            <NFormItem label="强制 UI 缩放">
              <NSwitch v-model:value="form.forceUIResizing" />
            </NFormItem>
          </div>
        </NCollapseItem>

        <NCollapseItem title="翻译行为" name="behaviour">
          <div class="collapse-grid">
            <NFormItem label="强制换行字符数">
              <NInputNumber v-model:value="form.forceSplitTextAfterCharacters" :min="0" :max="10000" />
            </NFormItem>
            <NFormItem label="文本解析最大递归">
              <NInputNumber v-model:value="form.maxTextParserRecursion" :min="1" :max="100" />
            </NFormItem>
            <NFormItem label="文本获取兼容模式">
              <NSwitch v-model:value="form.textGetterCompatibilityMode" />
            </NFormItem>
            <NFormItem label="翻译辅助器">
              <NSwitch v-model:value="form.enableTranslationHelper" />
            </NFormItem>
            <NFormItem label="数字模板化">
              <NSwitch v-model:value="form.templateAllNumberAway" />
            </NFormItem>
            <NFormItem label="禁用 TMP 滚动效果">
              <NSwitch v-model:value="form.disableTextMeshProScrollInEffects" />
            </NFormItem>
            <NFormItem label="缓存已解析翻译">
              <NSwitch v-model:value="form.cacheParsedTranslations" />
            </NFormItem>
            <NFormItem label="翻译输出文件">
              <NInput
                :value="form.outputFile ?? ''"
                @update:value="(v: string) => { form.outputFile = v }"
                placeholder="Translation\{Lang}\Text\_AutoGeneratedTranslations.txt" clearable
              />
            </NFormItem>
          </div>
        </NCollapseItem>

        <NCollapseItem title="贴图翻译" name="texture">
          <div class="collapse-grid">
            <NFormItem label="启用贴图翻译">
              <NSwitch v-model:value="form.enableTextureTranslation" />
            </NFormItem>
            <NFormItem label="启用贴图导出">
              <NSwitch v-model:value="form.enableTextureDumping" />
            </NFormItem>
            <NFormItem label="启用贴图切换">
              <NSwitch v-model:value="form.enableTextureToggling" />
            </NFormItem>
            <NFormItem label="场景加载时扫描贴图">
              <NSwitch v-model:value="form.enableTextureScanOnSceneLoad" />
            </NFormItem>
            <NFormItem label="加载未修改贴图">
              <NSwitch v-model:value="form.loadUnmodifiedTextures" />
            </NFormItem>
            <NFormItem label="启用 Sprite 钩子">
              <NSwitch v-model:value="form.enableSpriteHooking" />
            </NFormItem>
            <NFormItem label="贴图哈希策略">
              <NSelect v-model:value="form.textureHashGenerationStrategy" :options="textureHashOptions" />
            </NFormItem>
            <NFormItem label="贴图目录">
              <NInput
                :value="form.textureDirectory ?? ''"
                @update:value="(v: string) => { form.textureDirectory = v }"
                placeholder="Translation\{Lang}\Texture" clearable
              />
            </NFormItem>
          </div>
        </NCollapseItem>
      </NCollapse>

      <div class="config-footer">
        <NButton :disabled="disabled" @click="openConfigEditor">
          <template #icon>
            <NIcon><EditNoteOutlined /></NIcon>
          </template>
          编辑配置文件
        </NButton>
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

.api-key-group {
  margin-bottom: 8px;
}

.framework-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0 24px;
}

.config-collapse {
  margin-top: 8px;
}

.collapse-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0 24px;
}

.config-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
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

  .collapse-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 480px) {
  .framework-grid {
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
