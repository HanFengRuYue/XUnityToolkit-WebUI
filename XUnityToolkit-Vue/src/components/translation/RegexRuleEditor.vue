<script setup lang="ts">
import { computed } from 'vue'
import { NButton, NEmpty, NIcon, NInput, NSelect, NTag } from 'naive-ui'
import type { SelectOption } from 'naive-ui'
import { AddOutlined, DeleteOutlined } from '@vicons/material'
import type { RegexRuleKind, RegexRuleSection, RegexTranslationRule } from '@/api/types'

const props = defineProps<{
  rules: RegexTranslationRule[]
}>()

const emit = defineEmits<{
  'update:rules': [value: RegexTranslationRule[]]
}>()

const SECTION_ORDER: RegexRuleSection[] = ['base', 'custom', 'dynamic']

const SECTION_META: Record<RegexRuleSection, { label: string; hint: string; persistent: boolean }> = {
  base: {
    label: '基础规则',
    hint: '再次执行预翻译时会按当前系统规则重新生成。',
    persistent: false,
  },
  custom: {
    label: '自定义规则',
    hint: '再次执行预翻译时会保留这部分内容。',
    persistent: true,
  },
  dynamic: {
    label: '动态规则',
    hint: '再次执行预翻译时会按最新分析结果重新生成。',
    persistent: false,
  },
}

const kindOptions: SelectOption[] = [
  { label: 'sr', value: 'sr' },
  { label: 'r', value: 'r' },
]

const sections = computed<Record<RegexRuleSection, RegexTranslationRule[]>>(() => ({
  base: props.rules.filter(rule => rule.section === 'base'),
  custom: props.rules.filter(rule => rule.section === 'custom'),
  dynamic: props.rules.filter(rule => rule.section === 'dynamic'),
}))

function updateRules(nextRules: RegexTranslationRule[]) {
  emit('update:rules', nextRules)
}

function addCustomRule() {
  updateRules([
    ...props.rules,
    {
      id: `custom-${Date.now()}-${props.rules.length + 1}`,
      section: 'custom',
      kind: 'r',
      pattern: '',
      replacement: '',
    },
  ])
}

function removeRule(section: RegexRuleSection, sectionIndex: number) {
  const allRules = [...props.rules]
  let sectionCursor = -1
  const targetIndex = allRules.findIndex(rule => {
    if (rule.section !== section) return false
    sectionCursor++
    return sectionCursor === sectionIndex
  })
  if (targetIndex >= 0) {
    allRules.splice(targetIndex, 1)
    updateRules(allRules)
  }
}

function updateRule(section: RegexRuleSection, sectionIndex: number, patch: Partial<RegexTranslationRule>) {
  const allRules = [...props.rules]
  let sectionCursor = -1
  const targetIndex = allRules.findIndex(rule => {
    if (rule.section !== section) return false
    sectionCursor++
    return sectionCursor === sectionIndex
  })
  if (targetIndex < 0) return

  const current = allRules[targetIndex]!
  allRules[targetIndex] = {
    id: patch.id ?? current.id,
    section: patch.section ?? current.section,
    kind: patch.kind ?? current.kind,
    pattern: patch.pattern ?? current.pattern,
    replacement: patch.replacement ?? current.replacement,
  }
  updateRules(allRules)
}

function updateKind(section: RegexRuleSection, sectionIndex: number, value: string) {
  updateRule(section, sectionIndex, { kind: value as RegexRuleKind })
}
</script>

<template>
  <div class="regex-toolbar">
    <NButton size="small" type="primary" @click="addCustomRule">
      <template #icon><NIcon :size="16"><AddOutlined /></NIcon></template>
      新增自定义规则
    </NButton>
  </div>

  <div v-for="section in SECTION_ORDER" :key="section" class="regex-section">
    <div class="regex-section-header">
      <div>
        <div class="regex-section-title">
          {{ SECTION_META[section].label }}
          <NTag
            size="small"
            :bordered="false"
            :type="SECTION_META[section].persistent ? 'success' : 'warning'"
          >
            {{ sections[section].length }}
          </NTag>
        </div>
        <div class="regex-section-hint">{{ SECTION_META[section].hint }}</div>
      </div>
    </div>

    <div v-if="sections[section].length === 0" class="regex-empty">
      <NEmpty size="small" description="当前区块暂无规则" />
    </div>

    <div v-else class="regex-rule-list">
      <div
        v-for="(rule, sectionIndex) in sections[section]"
        :key="`${section}-${sectionIndex}-${rule.id}`"
        class="regex-rule-row"
      >
        <NSelect
          :value="rule.kind"
          :options="kindOptions"
          size="small"
          class="regex-kind"
          @update:value="updateKind(section, sectionIndex, $event as string)"
        />
        <NInput
          :value="rule.pattern"
          type="textarea"
          size="small"
          :autosize="{ minRows: 1, maxRows: 4 }"
          placeholder="正则表达式"
          class="regex-field"
          @update:value="updateRule(section, sectionIndex, { pattern: $event })"
        />
        <NInput
          :value="rule.replacement"
          type="textarea"
          size="small"
          :autosize="{ minRows: 1, maxRows: 4 }"
          placeholder="替换内容"
          class="regex-field"
          @update:value="updateRule(section, sectionIndex, { replacement: $event })"
        />
        <NButton size="small" quaternary type="error" @click="removeRule(section, sectionIndex)">
          <template #icon><NIcon :size="16"><DeleteOutlined /></NIcon></template>
        </NButton>
      </div>
    </div>
  </div>
</template>

<style scoped>
.regex-toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 12px;
}

.regex-section {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  padding: 12px;
  background: var(--bg-subtle);
}

.regex-section + .regex-section {
  margin-top: 12px;
}

.regex-section-header {
  margin-bottom: 10px;
}

.regex-section-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-1);
}

.regex-section-hint {
  margin-top: 4px;
  font-size: 12px;
  color: var(--text-3);
  line-height: 1.5;
}

.regex-empty {
  padding: 12px 0;
}

.regex-rule-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.regex-rule-row {
  display: grid;
  grid-template-columns: 92px minmax(0, 1fr) minmax(0, 1fr) auto;
  gap: 8px;
  align-items: flex-start;
}

.regex-kind {
  width: 92px;
}

.regex-field {
  width: 100%;
}

@media (max-width: 960px) {
  .regex-rule-row {
    grid-template-columns: 1fr;
  }

  .regex-kind {
    width: 100%;
  }
}
</style>
