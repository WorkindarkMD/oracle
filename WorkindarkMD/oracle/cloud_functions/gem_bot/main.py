import functions_framework
from google.cloud import firestore
from google.events.cloud import firestore as firestore_events
import datetime

# --- Конфигурация ---
GCP_PROJECT_ID = "projectoracle"
FIRESTORE_COLLECTION = "cbot_signals"

# Инициализация клиента Firestore
db = firestore.Client(project=GCP_PROJECT_ID)

@functions_framework.cloud_event
def gem_bot(cloud_event: firestore_events.DocumentEventData):
    """
    Firestore-триггер, который срабатывает при создании нового документа
    в коллекции 'cbot_signals'.

    Эта функция добавляет в документ подтверждение о том, что сигнал
    был получен и обработан Gem.Bot.
    """
    try:
        # Извлекаем ID документа из данных события
        document_id = cloud_event.document.split('/')[-1]

        # Создаем ссылку на документ, который вызвал событие
        doc_ref = db.collection(FIRESTORE_COLLECTION).document(document_id)

        # Данные для обновления
        update_data = {
            "gem_bot_ack": True,
            "ack_timestamp": datetime.datetime.now(datetime.timezone.utc).isoformat()
        }

        # Обновляем документ, добавляя новые поля
        doc_ref.update(update_data)

        print(f"Document {document_id} acknowledged by Gem.Bot.")

    except Exception as e:
        # Логируем любые ошибки, возникшие в процессе
        print(f"Error processing document {cloud_event.document}: {str(e)}")
