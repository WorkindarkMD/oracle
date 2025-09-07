### **Руководство по Развертыванию и Тестированию "Нервной Системы"**

**Ваша цель:** Увидеть на "Панели Стратега", как каждые 10 секунд появляется сообщение от cBot, в котором есть подтверждение от Gem.Bot.

---

#### **Часть 1: Развертывание Облачных Функций**

Для этого шага вам понадобится установленный [Google Cloud SDK](https://cloud.google.com/sdk/docs/install).

1.  **Откройте терминал или командную строку** на вашем компьютере.
2.  **Аутентифицируйтесь в gcloud:**
    ```bash
    gcloud auth login
    ```
3.  **Установите ваш проект как основной:**
    ```bash
    gcloud config set project projectoracle
    ```
4.  **Разверните функцию `api_gateway`:**
    *   Перейдите в директорию с функцией: `cd path/to/your/project/WorkindarkMD/oracle/cloud_functions/api_gateway`
    *   Выполните команду:
        ```bash
        gcloud functions deploy api-gateway --gen2 --runtime=python311 --region=europe-west3 --source=. --entry-point=api_gateway --trigger-http --allow-unauthenticated
        ```
    *   **ВАЖНО:** После выполнения команды в выводе будет строка `uri:`. **Скопируйте этот URL!** Он понадобится нам для cBot. Он будет выглядеть примерно так: `https://api-gateway-....run.app`.

5.  **Разверните функцию `gem_bot`:**
    *   Перейдите в директорию с функцией: `cd path/to/your/project/WorkindarkMD/oracle/cloud_functions/gem_bot`
    *   Выполните команду:
        ```bash
        gcloud functions deploy gem-bot --gen2 --runtime=python311 --region=europe-west3 --source=. --entry-point=gem_bot --trigger-event-filters="type=google.cloud.firestore.document.v1.created" --trigger-event-filters="database=(default)" --trigger-event-filters-path-pattern="documents=cbot_signals/{docId}"
        ```
    *   Эта функция не вернет URL, так как она запускается по событию в базе данных.

---

#### **Часть 2: Настройка и Запуск cBot "Оракул"**

1.  **Откройте файл** `WorkindarkMD/oracle/cbot/OracleBot.cs` в текстовом редакторе или прямо в cTrader.
2.  Найдите строку: `private const string ApiGatewayUrl = "ВАШ_URL_API_ШЛЮЗА_СЮДА";`
3.  **Замените** `"ВАШ_URL_API_ШЛЮЗА_СЮДА"` на тот **URL**, который вы скопировали на шаге 1.4.
4.  Сохраните файл.
5.  **Откройте платформу cTrader**, скомпилируйте и запустите робота `OracleBot` на любом графике. В журнале робота вы должны увидеть сообщения об отправке "heartbeat".

---

#### **Часть 3: Настройка и Просмотр "Панели Стратега"**

1.  **Откройте файл** `WorkindarkMD/oracle/strategist_panel/index.html` в текстовом редакторе.
2.  Найдите блок `const firebaseConfig = { ... };`.
3.  **Следуйте инструкции в комментариях** над этим блоком, чтобы получить ваш реальный объект `firebaseConfig` из консоли Firebase.
    *   *Кратко: Консоль Firebase -> Настройки проекта -> Your apps -> Web app -> SDK snippet -> Config.*
4.  **Замените** весь демонстрационный объект `firebaseConfig` на ваш реальный.
5.  Сохраните файл.
6.  **Откройте файл `index.html` в вашем веб-браузере** (просто двойным кликом по файлу).

---

#### **Часть 4: Финальная Проверка**

Теперь у вас одновременно запущен cBot и открыта "Панель Стратега" в браузере.

**Критерий успеха:**
*   Вы открываете `index.html`.
*   На странице появляется первое сообщение от cBot.
*   Буквально через мгновение это же сообщение **обновляется**, и в нем появляются два новых поля: `"gem_bot_ack": true` и `"ack_timestamp"`.
*   Каждые 10 секунд появляется новое, свежее сообщение, которое также обновляется.

Если вы это видите — **поздравляю!** "Нервная система" вашего проекта полностью функциональна.

Пожалуйста, сообщите мне о результатах этого теста.
