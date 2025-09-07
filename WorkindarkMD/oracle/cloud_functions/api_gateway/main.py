import os
import json
from google.cloud import firestore
import functions_framework

# --- Конфигурация ---
# В реальной продакшн-среде эти значения должны быть загружены
# из секретов (Secret Manager) или переменных окружения.
# Для простоты первого спринта мы вставляем их прямо сюда.
GCP_PROJECT_ID = "projectoracle"
VALID_API_KEY = "AIzaSyDjF2j9D8a92PxTQP_kLt325NsyQbqu5oY"
FIRESTORE_COLLECTION = "cbot_signals"

# Инициализация клиента Firestore
# Клиент автоматически использует учетные данные сервисного аккаунта,
# когда функция развернута в Google Cloud.
db = firestore.Client(project=GCP_PROJECT_ID)

@functions_framework.http
def api_gateway(request):
    """
    HTTP Cloud Function, которая выступает в роли API-шлюза.
    1. Проверяет API-ключ в заголовке 'X-API-KEY'.
    2. Если ключ валиден, сохраняет JSON-тело запроса в Firestore.
    3. Возвращает статус операции.
    """
    # 1. Проверка API-ключа
    api_key = request.headers.get("X-API-KEY")
    if not api_key or api_key != VALID_API_KEY:
        return ("Unauthorized: Missing or invalid API key", 401)

    # 2. Проверка метода запроса и наличия данных
    if request.method != 'POST':
        return ("Method Not Allowed", 405)

    request_json = request.get_json(silent=True)
    if not request_json:
        return ("Bad Request: Missing JSON payload", 400)

    try:
        # 3. Сохранение данных в Firestore
        # Мы не указываем ID документа, чтобы Firestore сгенерировал его автоматически.
        doc_ref = db.collection(FIRESTORE_COLLECTION).document()
        doc_ref.set(request_json)

        # Успешный ответ
        response_data = {
            "status": "success",
            "message": "Signal received and stored.",
            "document_id": doc_ref.id
        }
        return (json.dumps(response_data), 200, {'Content-Type': 'application/json'})

    except Exception as e:
        # Обработка возможных ошибок при работе с Firestore
        error_message = f"Internal Server Error: Could not write to Firestore. Details: {str(e)}"
        print(error_message) # Логируем ошибку для отладки
        return (error_message, 500)
