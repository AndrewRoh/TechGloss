#!/bin/bash
# Phase D 전환 시 기존 published 용어를 모두 임베딩해 Qdrant에 적재
# 실행 전 GlossaryApi와 Qdrant가 기동 중이어야 함

BASE_URL="${GLOSSARY_API:-http://127.0.0.1:5088}"

echo "=== 기존 published 용어 일괄 임베딩 시작 ==="

# 1. 현재 published 용어 목록 조회
entries=$(curl -s "$BASE_URL/glossary/lookup?q=&lang=auto&limit=1000" | \
          python3 -c "import sys,json; data=json.load(sys.stdin); \
          [print(e['id']) for e in data]")

if [ -z "$entries" ]; then
    echo "published 용어 없음 — 시드 데이터를 먼저 publish 하세요"
    exit 0
fi

count=0
while IFS= read -r entry_id; do
    [ -z "$entry_id" ] && continue
    # publish 엔드포인트를 재호출해 임베딩 + Qdrant upsert 트리거
    result=$(curl -s -X POST "$BASE_URL/glossary/publish" \
             -H "Content-Type: application/json" \
             -d "{\"EntryId\":\"$entry_id\"}")
    echo "[$((++count))] $entry_id → $result"
    sleep 0.1  # Ollama 과부하 방지
done <<< "$entries"

echo "=== 완료: $count 건 임베딩 ==="
