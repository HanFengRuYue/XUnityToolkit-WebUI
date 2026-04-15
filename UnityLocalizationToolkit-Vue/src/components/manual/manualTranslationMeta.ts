import type {
  ManualTranslationAssetFilterValueKind,
  ManualTranslationAssetStorageKind,
  ManualTranslationAssetValueKind,
  ManualTranslationKindCounts,
} from '@/api/types'

export const DEFAULT_MANUAL_PAGE_SIZE = 50
export const DEFAULT_MANUAL_KIND: ManualTranslationAssetFilterValueKind = 'All'

export interface ManualKindMeta {
  value: ManualTranslationAssetFilterValueKind
  label: string
  description: string
}

export const MANUAL_KIND_OPTIONS: ManualKindMeta[] = [
  { value: 'All', label: '全部', description: '查看当前项目下的全部手动翻译资源。' },
  { value: 'Text', label: '文本', description: '编辑 TextAsset 和 MonoBehaviour 中的文本字段。' },
  { value: 'Code', label: '代码', description: '处理托管程序集和二进制中的字符串字面量。' },
  { value: 'Image', label: '贴图', description: '预览原始贴图，并上传替换图。' },
  { value: 'Font', label: '字体', description: '进入专用字体面板，选择或恢复替换源。' },
  { value: 'Binary', label: '二进制', description: '查看元数据并导出原始资源信息。' },
]

export const MANUAL_PAGE_SIZE_OPTIONS = [25, 50, 100, 200].map(value => ({
  label: `${value} / 页`,
  value,
}))

const STORAGE_KIND_LABELS: Record<ManualTranslationAssetStorageKind, string> = {
  LooseAssetFile: '原始资源文件',
  BundleEntry: '资源包条目',
  ManagedAssembly: '托管程序集',
  ManagedResource: '托管资源',
  Il2CppMetadata: 'IL2CPP 元数据',
  NativeBinary: '原生二进制',
}

export function getManualKindLabel(kind: ManualTranslationAssetFilterValueKind | ManualTranslationAssetValueKind) {
  return MANUAL_KIND_OPTIONS.find(option => option.value === kind)?.label ?? kind
}

export function getManualKindDescription(kind: ManualTranslationAssetFilterValueKind | ManualTranslationAssetValueKind) {
  return MANUAL_KIND_OPTIONS.find(option => option.value === kind)?.description ?? '未定义的资源类型。'
}

export function getManualStorageKindLabel(kind: ManualTranslationAssetStorageKind) {
  return STORAGE_KIND_LABELS[kind] ?? kind
}

export function getManualKindCount(kind: ManualTranslationAssetFilterValueKind, counts?: ManualTranslationKindCounts | null) {
  if (!counts)
    return 0

  switch (kind) {
    case 'All':
      return counts.all
    case 'Text':
      return counts.text
    case 'Code':
      return counts.code
    case 'Image':
      return counts.image
    case 'Font':
      return counts.font
    case 'Binary':
      return counts.binary
    default:
      return 0
  }
}

export function getManualValueKindTagType(kind: ManualTranslationAssetValueKind) {
  switch (kind) {
    case 'Text':
      return 'success'
    case 'Code':
      return 'info'
    case 'Image':
      return 'warning'
    case 'Font':
      return 'primary'
    case 'Binary':
      return 'default'
    default:
      return 'default'
  }
}

export function formatManualWorkspaceState(state?: string | null) {
  switch (state) {
    case 'ready':
      return '可编辑'
    case 'dirty':
      return '待应用'
    case 'applied':
      return '已写回'
    case 'notScanned':
      return '未扫描'
    default:
      return state || '未知'
  }
}
