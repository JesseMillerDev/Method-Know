#!/bin/bash
API_URL="http://localhost:5191"
EMAIL="tagtest_$(date +%s)@example.com"
PASSWORD="Password123!"

echo "1. Signup..."
SIGNUP_RES=$(curl -s -X POST "$API_URL/api/auth/signup" -H "Content-Type: application/json" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")
echo "Signup Response: $SIGNUP_RES"

echo "2. Login..."
LOGIN_RES=$(curl -s -X POST "$API_URL/api/auth/login" -H "Content-Type: application/json" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")
TOKEN=$(echo $LOGIN_RES | jq -r '.token')
USER_ID=$(echo $SIGNUP_RES | jq -r '.id')
echo "Token: $TOKEN"

if [ "$TOKEN" == "null" ]; then
    echo "Login failed"
    exit 1
fi

echo "3. Create Article about C#..."
CONTENT="C# is a modern, object-oriented, and type-safe programming language. C# enables developers to build many types of secure and robust applications that run in .NET."
ARTICLE_RES=$(curl -s -X POST "$API_URL/api/articles" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "{\"title\":\"Intro to C#\",\"content\":\"$CONTENT\",\"userId\":\"$USER_ID\"}")
echo "Raw Response: $ARTICLE_RES"
ARTICLE_ID=$(echo $ARTICLE_RES | jq -r '.id')
TAGS=$(echo $ARTICLE_RES | jq -r '.tagList')
echo "Article ID: $ARTICLE_ID"
echo "Tags: $TAGS"

# Verify tags contain C# or .NET
if [[ "$TAGS" == *"C#"* ]] || [[ "$TAGS" == *".NET"* ]]; then
    echo "SUCCESS: Tags generated correctly."
else
    echo "FAILURE: Tags missing or incorrect."
fi

echo "4. Update Article content..."
NEW_CONTENT="Python is a high-level, general-purpose programming language. Its design philosophy emphasizes code readability with the use of significant indentation."
UPDATE_RES=$(curl -s -X PUT "$API_URL/api/articles/$ARTICLE_ID" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "{\"title\":\"Intro to EF Core\",\"content\":\"$NEW_CONTENT\",\"userId\":\"$USER_ID\"}")
echo "Raw Update Response: $UPDATE_RES"
NEW_TAGS=$(echo $UPDATE_RES | jq -r '.tagList')
echo "New Tags: $NEW_TAGS"

if [[ "$NEW_TAGS" == *"Python"* ]]; then
    echo "SUCCESS: Tags updated correctly."
else
    echo "FAILURE: Tags not updated."
fi
