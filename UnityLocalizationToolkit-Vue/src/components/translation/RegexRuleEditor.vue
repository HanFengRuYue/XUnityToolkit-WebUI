<script setup lang="ts">
import { computed } from 'vue'
import { NButton, NEmpty, NIcon, NInput, NSelect, NTag } from 'naive-ui'
import type { SelectOption } from 'naive-ui'
import { AddOutlined, DeleteOutlined } from '@vicons/material'
import type { RegexRuleKind, RegexRuleSection, RegexTranslationRule } from '@/api/types'

const props = defineProps<{
  rules: RegexTranslationRule[]
  searchKeyword?: string
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

const normalizedSearchKeyword = computed(() => props.searchKeyword?.trim().toLowerCase() ?? '')
const hasSearchKeyword = computed(() => normalizedSearchKeyword.value.length > 0)

const sections = computed<Record<RegexRuleSection, RegexTranslationRule[]>>(() => ({
  base: props.rules.filter(rule => rule.section === 'base'),
  custom: props.rules.filter(rule => rule.section === 'custom'),
  dynamic: props.rules.filter(rule => rule.section === 'dynamic'),
}))

const filteredSections = computed<Record<RegexRuleSection, RegexTranslationRule[]>>(() => ({
  base: sections.value.base.filter(rule => matchesRule(rule, normalizedSearchKeyword.value)),
  custom: sections.value.custom.filter(rule => matchesRule(rule, normalizedSearchKeyword.value)),
  dynamic: sections.value.dynamic.filter(rule => matchesRule(rule, normalizedSearchKeyword.value)),
}))

const hasMatchedRules = computed(() =>
  SECTION_ORDER.some(section => filteredSections.value[section].length > 0),
)

function updateRules(nextRules: RegexTranslationRule[]) {
  emit('update:rules', nextRules)
}

function matchesRule(rule: RegexTranslationRule, keyword: string) {
  if (!keyword) return true

  return rule.pattern.toLowerCase().includes(keyword)
    || rule.replacement.toLowerCase().includes(keyword)
    || rule.kind.toLowerCase().includes(keyword)
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

function findRuleIndex(ruleId: string) {
  return props.rules.findIndex(rule => rule.id === ruleId)
}

function removeRule(ruleId: string) {
  const allRules = [...props.rules]
  const targetIndex = findRuleIndex(ruleId)
  if (targetIndex >= 0) {
    allRules.splice(targetIndex, 1)
    updateRules(allRules)
  }
}

function updateRule(ruleId: string, patch: Partial<RegexTranslationRule>) {
  const allRules = [...props.rules]
  const targetIndex = findRuleIndex(ruleId)
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

function updateKind(ruleId: string, value: string) {
  updateRule(ruleId, { kind: value as RegexRuleKind })
}

function getSectionEmptyDescription() {
  if (hasSearchKeyword.value) {
    return '当前区块没有匹配的规则'
  }

  return '当前区块暂无规则'
}
</script>

<template>
  <div class="regex-toolbar">
    <NButton size="small" type="primary" @click="addCustomRule">
      <template #icon><NIcon :size="16"><AddOutlined /></NIcon></template>
      新增自定义规则
    </NButton>
  </div>

  <div v-if="hasSearchKeyword && !hasMatchedRules" class="regex-empty">
    <NEmpty description="没有匹配的正则规则" />
  </div>

  <template v-else>
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
              {{ hasSearchKeyword ? `${filteredSections[section].length} / ${sections[section].length}` : sections[section].length }}
            </NTag>
          </div>
          <div class="regex-section-hint">{{ SECTION_META[section].hint }}</div>
        </div>
      </div>

      <div v-if="filteredSections[section].length === 0" class="regex-empty">
        <NEmpty size="small" :description="getSectionEmptyDescription()" />
      </div>

      <div v-else class="regex-rule-list">
        <div
          v-for="rule in filteredSections[section]"
          :key="rule.id"
          class="regex-rule-row"
        >
          <NSelect
            :value="rule.kind"
            :options="kindOptions"
            size="small"
            class="regex-kind"
            @update:value="updateKind(rule.id, $event as string)"
          />
          <NInput
            :value="rule.pattern"
            type="textarea"
            size="small"
            :autosize="{ minRows: 1, maxRows: 4 }"
            placeholder="正则表达式"
            class="regex-field"
            @update:value="updateRule(rule.id, { pattern: $event })"
          />
          <NInput
            :value="rule.replacement"
            type="textarea"
            size="small"
            :autosize="{ minRows: 1, maxRows: 4 }"
            placeholder="替换内容"
            class="regex-field"
            @update:value="updateRule(rule.id, { replacement: $event })"
          />
          <NButton size="small" quaternary type="error" @click="removeRule(rule.id)">
            <template #icon><NIcon :size="16"><DeleteOutlined /></NIcon></template>
          </NButton>
        </div>
      </div>
    </div>
  </template>
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
