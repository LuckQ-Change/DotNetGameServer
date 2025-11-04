document.addEventListener('DOMContentLoaded', function () {
    const statusGrid = document.getElementById('status-grid');
    const modal = document.getElementById('details-modal');
    const modalContent = document.getElementById('modal-text');
    const closeModal = document.getElementsByClassName('close')[0];
    const connectionStatus = document.getElementById('connection-status');
    const lastUpdate = document.getElementById('last-update');

    const statusClasses = {
        'Healthy': 'status-healthy',
        'Unhealthy': 'status-unhealthy',
        'Degraded': 'status-degraded'
    };

    const statusTranslations = {
        'Healthy': '健康',
        'Unhealthy': '异常',
        'Degraded': '降级'
    };

    function renderServerStatus(healthReport) {
        statusGrid.innerHTML = '';
        if (!healthReport || !healthReport.checks) {
            statusGrid.innerHTML = '<p>未获取到服务器状态数据。</p>';
            return;
        }

        healthReport.checks.forEach(service => {
            const card = document.createElement('div');
            card.className = 'service-card';

            const statusClass = statusClasses[service.status] || 'status-degraded';
            const translatedStatus = statusTranslations[service.status] || '未知';

            let cardContent = `
                <div class="service-header">
                    <span>${service.name}</span>
                    <span class="service-status ${statusClass}">${translatedStatus}</span>
                </div>
                <div class="service-details">
                    <p><strong>状态:</strong> ${service.description || '暂无描述'}</p>
                </div>
            `;

            if (service.name === 'Resource Server') {
                cardContent += `
                    <div class="service-actions">
                        <button class="manage-btn">管理</button>
                    </div>
                `;
            }

            card.innerHTML = cardContent;

            card.addEventListener('click', (e) => {
                if (e.target.classList.contains('manage-btn')) {
                    return;
                }
                let detailsHtml = `
                    <h2>${service.name} - 状态详情</h2>
                    <p><strong>状态:</strong> ${translatedStatus} (${service.status})</p>
                    <p><strong>描述:</strong> ${service.description || '无'}</p>
                `;
                if (service.exception) {
                    detailsHtml += `<p><strong>异常信息:</strong></p><pre>${service.exception}</pre>`;
                }
                if (service.data && Object.keys(service.data).length > 0) {
                    detailsHtml += `<p><strong>附加数据:</strong></p><pre>${JSON.stringify(service.data, null, 2)}</pre>`;
                }
                modalContent.innerHTML = detailsHtml;
                modal.style.display = 'block';
            });

            if (service.name === 'Resource Server') {
                const manageBtn = card.querySelector('.manage-btn');
                if (manageBtn) {
                    manageBtn.addEventListener('click', (e) => {
                        e.stopPropagation();
                        window.open('/resource-management.html', '_blank');
                    });
                }
            }

            statusGrid.appendChild(card);
        });
        lastUpdate.textContent = `最近更新: ${new Date().toLocaleTimeString()}`;
    }

    async function fetchServerStatusOnce() {
        try {
            const response = await fetch('/api/server-status');
            const healthReport = await response.json();
            renderServerStatus(healthReport);
            connectionStatus.textContent = '轮询刷新';
        } catch (error) {
            console.error('无法获取服务器状态:', error);
            statusGrid.innerHTML = '<p>加载服务器状态失败，请检查后端服务是否正常。</p>';
            connectionStatus.textContent = '连接失败';
        }
    }

    // 优先使用 SSE 实时更新，失败时降级为定时轮询
    let sse;
    try {
        sse = new EventSource('/api/server-status/stream');
        connectionStatus.textContent = '实时连接中...';
        sse.onopen = () => connectionStatus.textContent = '实时连接已建立';
        sse.onmessage = (e) => {
            try {
                const data = JSON.parse(e.data);
                renderServerStatus(data);
            } catch (err) {
                console.error('解析实时数据失败:', err);
            }
        };
        sse.onerror = () => {
            connectionStatus.textContent = '实时连接中断，切换为轮询';
            sse.close();
            // 降级为轮询模式
            fetchServerStatusOnce();
            setInterval(fetchServerStatusOnce, 5000); // 每5秒刷新一次
        };
    } catch (e) {
        console.warn('SSE 不可用，使用轮询模式:', e);
        fetchServerStatusOnce();
        setInterval(fetchServerStatusOnce, 5000);
    }

    closeModal.onclick = function() {
        modal.style.display = 'none';
    }

    window.onclick = function(event) {
        if (event.target == modal) {
            modal.style.display = 'none';
        }
    }
});