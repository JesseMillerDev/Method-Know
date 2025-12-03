#!/bin/bash
API_URL="http://localhost:5191"
EMAIL="testuser_1764753062@example.com"
PASSWORD="Password123!"

echo "1. Signup..."
SIGNUP_RES=$(curl -s -X POST "$API_URL/api/auth/signup" -H "Content-Type: application/json" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")
echo "Signup Response: $SIGNUP_RES"

echo "2. Login..."
LOGIN_RES=$(curl -s -X POST "$API_URL/api/auth/login" -H "Content-Type: application/json" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")
TOKEN=$(echo $LOGIN_RES | jq -r '.token')
echo "Token: $TOKEN"

if [ "$TOKEN" == "null" ]; then
    echo "Login failed"
    exit 1
fi

echo "3. Create Article..."
ARTICLE_RES=$(curl -s -X POST "$API_URL/api/articles" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "{\"title\":\"Test Article\",\"content\":\"This is a test article for voting.\",\"userId\":\"test\"}")
ARTICLE_ID=$(echo $ARTICLE_RES | jq -r '.id')
echo "Article ID: $ARTICLE_ID"

echo "4. Vote Up..."
curl -s -X POST "$API_URL/api/articles/$ARTICLE_ID/vote?voteValue=1" -H "Authorization: Bearer $TOKEN"
echo ""

echo "5. Check Stats (Expect Score=1, Up=1, Down=0)..."
curl -s -H "Authorization: Bearer $TOKEN" "$API_URL/api/articles" | jq ".[] | select(.id == $ARTICLE_ID) | {id, score, upvotes, downvotes, userVoteValue}"

echo "6. Vote Down (Change vote)..."
curl -s -X POST "$API_URL/api/articles/$ARTICLE_ID/vote?voteValue=-1" -H "Authorization: Bearer $TOKEN"
echo ""

echo "7. Check Stats (Expect Score=-1, Up=0, Down=1)..."
curl -s -H "Authorization: Bearer $TOKEN" "$API_URL/api/articles" | jq ".[] | select(.id == $ARTICLE_ID) | {id, score, upvotes, downvotes, userVoteValue}"

echo "8. Toggle Vote (Vote Down again to remove)..."
curl -s -X POST "$API_URL/api/articles/$ARTICLE_ID/vote?voteValue=-1" -H "Authorization: Bearer $TOKEN"
echo ""

echo "9. Check Stats (Expect Score=0, Up=0, Down=0)..."
curl -s -H "Authorization: Bearer $TOKEN" "$API_URL/api/articles" | jq ".[] | select(.id == $ARTICLE_ID) | {id, score, upvotes, downvotes, userVoteValue}"

