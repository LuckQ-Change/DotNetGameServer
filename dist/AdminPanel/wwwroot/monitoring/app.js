document.addEventListener('DOMContentLoaded', function () {
    const statusGrid = document.getElementById('status-grid');
    const modal = document.getElementById('details-modal');
    const modalContent = document.getElementById('modal-text');
    const closeModal = document.getElementsByClassName('close')[0];

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

    async function fetchServerStatus() {
        try {
            const response = await fetch('/api/server-status');
            const healthReport = await response.json();

            statusGrid.innerHTML = ''; // 清空现有内容

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

                // 如果是ResourceServer，添加管理按钮
                if (service.name === 'ResourceServer') {
                    cardContent += `
                        <div class="service-actions">
                            <button class="manage-btn">管理</button>
                        </div>
                    `;
                }

                card.innerHTML = cardContent;

                // 为整个卡片（除按钮外）添加点击事件监听器
                card.addEventListener('click', (e) => {
                    // 如果点击的是管理按钮，则不显示模态框
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

                // 如果是ResourceServer，为管理按钮添加单独的事件监听器
                if (service.name === 'ResourceServer') {
                    const manageBtn = card.querySelector('.manage-btn');
                    if (manageBtn) {
                        manageBtn.addEventListener('click', (e) => {
                            e.stopPropagation(); // 阻止事件冒泡到卡片
                            window.open('/resource-management.html', '_blank');
                        });
                    }
                }

                statusGrid.appendChild(card);
            });

        } catch (error) {
            console.error('无法获取服务器状态:', error);
            statusGrid.innerHTML = '<p>加载服务器状态失败，请检查后端服务是否正常。</p>';
        }
    }

    closeModal.onclick = function() {
        modal.style.display = 'none';
    }

    window.onclick = function(event) {
        if (event.target == modal) {
            modal.style.display = 'none';
        }
    }

    // 定期刷新状态
    fetchServerStatus();
    setInterval(fetchServerStatus, 15000); // 每15秒刷新一次
});